using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace SprintBoot.Issues;

/// <summary>A file placed inside rich text. It lives exactly as long as the text that references it.</summary>
public class RichTextFile : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    /// <summary>The issue or comment using it. Null until that text is saved.</summary>
    public Guid? OwnerId { get; set; }

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long Size { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Set in the saving transaction; the blob goes once that commits, or later by the worker.</summary>
    public DateTime? RemovedAt { get; set; }

    public string StorageKey => $"rich-text/{Id:N}";

    protected RichTextFile()
    {
    }

    public RichTextFile(Guid id, Guid projectId, string fileName, string contentType, long size, DateTime createdAt)
        : base(id)
    {
        ProjectId = projectId;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        CreatedAt = createdAt;
    }
}

/// <summary>How file addresses look inside stored and served HTML.</summary>
public static partial class RichTextFileUrls
{
    public static string Path(Guid id) => $"/api/v1/rich-text-file/{id}/content";

    /// <summary>Signatures expire, so stored text never keeps them.</summary>
    public static string StripSignatures(string html) => Address().Replace(html, match => match.Groups[1].Value + "/content");

    public static string Rewrite(string html, Func<Guid, string> toQuery) =>
        Address().Replace(html, match =>
        {
            var id = Guid.Parse(match.Groups[2].Value);
            return $"{Path(id)}?{toQuery(id)}";
        });

    public static HashSet<Guid> Ids(string? html) =>
        [.. Address().Matches(html ?? string.Empty).Select(match => Guid.Parse(match.Groups[2].Value))];

    [GeneratedRegex(@"(/api/v1/rich-text-file/([0-9a-fA-F-]{36}))/content(\?t=[A-Za-z0-9_-]*)?")]
    private static partial Regex Address();
}

public class RichTextFileStore(
    IRepository<RichTextFile, Guid> files,
    IServiceProvider services,
    IDataProtectionProvider protection,
    IUnitOfWorkManager unitOfWork,
    IClock clock,
    ILogger<RichTextFileStore> logger) : ITransientDependency
{
    private static readonly TimeSpan SignatureLifetime = TimeSpan.FromHours(12);

    private ITimeLimitedDataProtector Protector =>
        protection.CreateProtector("SprintBoot.RichTextFile").ToTimeLimitedDataProtector();

    public string Sign(string? html) =>
        string.IsNullOrEmpty(html) ? html ?? string.Empty : RichTextFileUrls.Rewrite(html, id => $"t={Protector.Protect(id.ToString(), SignatureLifetime)}");

    public string SignedUrl(Guid id) => $"{RichTextFileUrls.Path(id)}?t={Protector.Protect(id.ToString(), SignatureLifetime)}";

    public bool IsSigned(Guid id, string? token)
    {
        try
        {
            return !string.IsNullOrEmpty(token) && Protector.Unprotect(token) == id.ToString();
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Call after saving text: claims new files and removes those the text no longer uses.</summary>
    public async Task SyncAsync(Guid projectId, Guid ownerId, string? html)
    {
        var referenced = RichTextFileUrls.Ids(html);
        var owned = await files.GetListAsync(file => file.OwnerId == ownerId && file.RemovedAt == null);
        await RemoveAsync(owned.Where(file => !referenced.Contains(file.Id)).ToList());

        var ids = referenced.ToList();
        foreach (var file in await files.GetListAsync(file => ids.Contains(file.Id) && file.OwnerId == null && file.ProjectId == projectId && file.RemovedAt == null))
        {
            file.OwnerId = ownerId;
            await files.UpdateAsync(file);
        }
    }

    public async Task RemoveOwnedByAsync(IReadOnlyCollection<Guid> ownerIds)
    {
        var ids = ownerIds.ToList();
        await RemoveAsync(await files.GetListAsync(file => file.OwnerId != null && ids.Contains(file.OwnerId.Value) && file.RemovedAt == null));
    }

    private async Task RemoveAsync(List<RichTextFile> list)
    {
        if (list.Count == 0)
        {
            return;
        }

        foreach (var file in list)
        {
            file.RemovedAt = clock.Now;
            await files.UpdateAsync(file);
        }

        var ids = list.Select(file => file.Id).ToList();
        unitOfWork.Current!.OnCompleted(() => PurgeAsync(ids));
    }

    /// <summary>A failure leaves the row marked, so the worker tries again and no blob is lost track of.</summary>
    public async Task PurgeAsync(IReadOnlyCollection<Guid> ids)
    {
        foreach (var id in ids)
        {
            try
            {
                using var uow = unitOfWork.Begin(requiresNew: true);
                var file = await files.FindAsync(id);
                if (file is not null)
                {
                    // Resolved here: the mapper needs this store in tools that have no storage configured.
                    await services.GetRequiredService<IBlobContainer<AttachmentContainer>>().DeleteAsync(file.StorageKey);
                    await files.DeleteAsync(file);
                }

                await uow.CompleteAsync();
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                logger.LogWarning(failure, "Rich text file {FileId} could not be removed yet", id);
            }
        }
    }
}

[Authorize]
public class RichTextFileAppService(
    IRepository<RichTextFile, Guid> files,
    IBlobContainer<AttachmentContainer> container,
    IProjectAccess projectAccess,
    RichTextFileStore store,
    IConfiguration configuration,
    IClock clock)
    : ApplicationService, IRichTextFileAppService
{
    public async Task<RichTextFileDto> UploadAsync(Guid projectId, IRemoteStreamContent file)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.EditIssues);

        AttachmentRecorder.CheckSize(file.ContentLength, configuration);

        var stored = new RichTextFile(
            GuidGenerator.Create(), projectId, Path.GetFileName(file.FileName ?? "file"), file.ContentType ?? "application/octet-stream", file.ContentLength.Value, clock.Now);
        await using var stream = file.GetStream();
        await container.SaveAsync(stored.StorageKey, stream);
        await files.InsertAsync(stored, autoSave: true);

        return new RichTextFileDto
        {
            Id = stored.Id,
            Url = store.SignedUrl(stored.Id),
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            Size = stored.Size,
        };
    }
}

/// <summary>Anonymous because img and video tags cannot send a token; the signature proves a reader was allowed.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/rich-text-file")]
[AllowAnonymous]
public class RichTextFileController(
    IRepository<RichTextFile, Guid> files,
    IBlobContainer<AttachmentContainer> container,
    RichTextFileStore store) : ControllerBase
{
    private static readonly string[] Inline = ["image/png", "image/jpeg", "image/gif", "image/webp", "video/mp4", "video/webm", "video/ogg"];

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> ContentAsync(Guid id, string? t)
    {
        if (!store.IsSigned(id, t))
        {
            return Forbid();
        }

        var file = await files.FindAsync(id);
        if (file is null || file.RemovedAt is not null)
        {
            return NotFound();
        }

        var stream = await container.GetAsync(file.StorageKey);
        Response.Headers.XContentTypeOptions = "nosniff";

        // Anything else downloads, so uploaded HTML or SVG never runs on our origin.
        return Inline.Contains(file.ContentType)
            ? File(stream, file.ContentType, enableRangeProcessing: stream.CanSeek)
            : File(stream, "application/octet-stream", file.FileName);
    }
}

public class RichTextFileCleanupWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int HourMilliseconds = 60 * 60 * 1000;
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(24);

    public RichTextFileCleanupWorker(AbpAsyncTimer timer, IServiceScopeFactory scopeFactory)
        : base(timer, scopeFactory)
    {
        timer.Period = HourMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        await using var handle = await services.GetRequiredService<IAbpDistributedLock>().TryAcquireAsync(nameof(RichTextFileCleanupWorker));
        if (handle is not null)
        {
            await CleanAsync(services, services.GetRequiredService<IClock>().Now - AbandonedAfter);
        }
    }

    /// <summary>Removes files marked earlier, and uploads whose text was never saved.</summary>
    public static async Task CleanAsync(IServiceProvider services, DateTime abandonedBefore)
    {
        var files = services.GetRequiredService<IRepository<RichTextFile, Guid>>();
        List<Guid> ids;
        using (var uow = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var due = await files.GetListAsync(file => file.RemovedAt != null || (file.OwnerId == null && file.CreatedAt < abandonedBefore));
            ids = [.. due.Select(file => file.Id)];
            await uow.CompleteAsync();
        }

        await services.GetRequiredService<RichTextFileStore>().PurgeAsync(ids);
    }
}
