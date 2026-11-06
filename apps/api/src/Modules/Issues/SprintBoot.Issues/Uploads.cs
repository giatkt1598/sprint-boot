using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SprintBoot.Identity;
using SprintBoot.Platform;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.Aws;
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

/// <summary>A file on its way up in parts. It becomes an attachment or a rich text file when it completes.</summary>
public class UploadSession : Entity<Guid>
{
    public UploadPurpose Purpose { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid? IssueId { get; private set; }

    /// <summary>The id the attachment or rich text file gets, which also names the blob.</summary>
    public Guid FileId { get; private set; }

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long Size { get; private set; }

    public long PartSize { get; private set; }

    public Guid CreatorId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    /// <summary>The multipart upload object storage opened; null when parts go through the API.</summary>
    public string? StorageUploadId { get; set; }

    public int PartCount => (int)((Size + PartSize - 1) / PartSize);

    /// <summary>Matches <see cref="Attachment.StorageKey"/> and <see cref="RichTextFile.StorageKey"/>.</summary>
    public string StorageKey => Purpose == UploadPurpose.RichText ? $"rich-text/{FileId:N}" : FileId.ToString("N");

    protected UploadSession()
    {
    }

    public UploadSession(
        Guid id, UploadPurpose purpose, Guid projectId, Guid? issueId, Guid fileId, string fileName, string contentType,
        long size, long partSize, Guid creatorId, DateTime createdAt, DateTime expiresAt)
        : base(id)
    {
        Purpose = purpose;
        ProjectId = projectId;
        IssueId = issueId;
        FileId = fileId;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        PartSize = partSize;
        CreatorId = creatorId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    /// <summary>The bytes part <paramref name="number"/> must have: every part is full but the last.</summary>
    public long PartLength(int number) => Math.Min(PartSize, Size - ((number - 1) * PartSize));
}

/// <summary>Where the parts of an upload go, as the configured storage provider allows.</summary>
public interface IUploadStorage
{
    /// <summary>True when the browser sends parts straight to storage through presigned URLs.</summary>
    bool Direct { get; }

    /// <summary>Returns the storage's own upload id, if it has one.</summary>
    Task<string?> BeginAsync(UploadSession session);

    string? PartUrl(UploadSession session, int number, DateTime expiresAt);

    /// <summary>Only for storage that is not direct. Returns the part's ETag.</summary>
    Task<string> WritePartAsync(UploadSession session, int number, Stream content);

    /// <summary>Joins the parts into the blob and returns its size as stored.</summary>
    Task<long> CompleteAsync(UploadSession session, IReadOnlyList<UploadedPartDto> parts);

    Task AbortAsync(UploadSession session);
}

/// <summary>Disk storage: parts are written in place into one file, which becomes the blob at the end.</summary>
public class LocalUploadStorage(IConfiguration configuration, IBlobContainer<AttachmentContainer> container) : IUploadStorage
{
    public bool Direct => false;

    private string Folder(UploadSession session)
    {
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        return Path.Combine(Path.GetFullPath(storage.Local.Path), ".uploads", session.Id.ToString("N"));
    }

    public Task<string?> BeginAsync(UploadSession session)
    {
        Directory.CreateDirectory(Folder(session));
        return Task.FromResult<string?>(null);
    }

    public string? PartUrl(UploadSession session, int number, DateTime expiresAt) => null;

    public async Task<string> WritePartAsync(UploadSession session, int number, Stream content)
    {
        var folder = Folder(session);
        if (!Directory.Exists(folder))
        {
            throw new BusinessException(UploadErrors.Expired);
        }

        var expected = session.PartLength(number);
        var written = 0L;
        await using (var data = new FileStream(Path.Combine(folder, "data"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
        {
            data.Seek((number - 1) * session.PartSize, SeekOrigin.Begin);
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer)) > 0)
            {
                written += read;
                if (written > expected)
                {
                    throw new BusinessException(UploadErrors.PartInvalid);
                }

                await data.WriteAsync(buffer.AsMemory(0, read));
            }
        }

        if (written != expected)
        {
            throw new BusinessException(UploadErrors.PartInvalid);
        }

        var tag = $"{number}-{written}";
        await File.WriteAllTextAsync(Path.Combine(folder, $"{number}.done"), tag);
        return tag;
    }

    public async Task<long> CompleteAsync(UploadSession session, IReadOnlyList<UploadedPartDto> parts)
    {
        var folder = Folder(session);
        var path = Path.Combine(folder, "data");
        var missing = Enumerable.Range(1, session.PartCount).Any(number => !File.Exists(Path.Combine(folder, $"{number}.done")));
        if (missing || !File.Exists(path) || new FileInfo(path).Length != session.Size)
        {
            throw new BusinessException(UploadErrors.Incomplete);
        }

        await using (var data = File.OpenRead(path))
        {
            await container.SaveAsync(session.StorageKey, data, overrideExisting: true);
        }

        Directory.Delete(folder, recursive: true);
        return session.Size;
    }

    public Task AbortAsync(UploadSession session)
    {
        var folder = Folder(session);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Object storage: a multipart upload whose parts the browser PUTs straight to presigned URLs, so file bytes never pass
/// through the API or the proxy in front of it.
/// </summary>
public class S3UploadStorage(
    IBlobContainerConfigurationProvider configurations,
    IBlobNormalizeNamingService naming,
    IAwsBlobNameCalculator names,
    IAmazonS3ClientFactory clients,
    IConfiguration configuration) : IUploadStorage
{
    private static readonly string ContainerName = BlobContainerNameAttribute.GetContainerName<AttachmentContainer>();

    public bool Direct => true;

    /// <summary>The bucket and key the blob container itself uses, so the finished file reads back like any other.</summary>
    private (string Bucket, string Key, AwsBlobProviderConfiguration Aws) Locate(UploadSession session)
    {
        var containerConfiguration = configurations.Get(ContainerName);
        var normalized = naming.NormalizeNaming(containerConfiguration, ContainerName, session.StorageKey);
        var aws = containerConfiguration.GetAwsConfiguration();
        var bucket = string.IsNullOrWhiteSpace(aws.ContainerName)
            ? normalized.ContainerName!
            : naming.NormalizeContainerName(containerConfiguration, aws.ContainerName!);
        var key = names.Calculate(new BlobProviderGetArgs(normalized.ContainerName!, containerConfiguration, normalized.BlobName!));
        return (bucket, key, aws);
    }

    public async Task<string?> BeginAsync(UploadSession session)
    {
        var (bucket, key, aws) = Locate(session);
        using var client = await clients.GetAmazonS3Client(aws);
        var started = await client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = bucket,
            Key = key,
            ContentType = session.ContentType,
        });
        return started.UploadId;
    }

    public string? PartUrl(UploadSession session, int number, DateTime expiresAt)
    {
        var (bucket, key, aws) = Locate(session);
        using var client = AsyncHelper.RunSync(() => clients.GetAmazonS3Client(aws));
        var endpoint = configuration["Storage:S3:Endpoint"];
        return client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expiresAt.ToUniversalTime(),
            UploadId = session.StorageUploadId,
            PartNumber = number,
            Protocol = endpoint?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true ? Protocol.HTTP : Protocol.HTTPS,
        });
    }

    public Task<string> WritePartAsync(UploadSession session, int number, Stream content) =>
        throw new BusinessException(UploadErrors.PartInvalid);

    public async Task<long> CompleteAsync(UploadSession session, IReadOnlyList<UploadedPartDto> parts)
    {
        var (bucket, key, aws) = Locate(session);
        using var client = await clients.GetAmazonS3Client(aws);
        try
        {
            await client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
            {
                BucketName = bucket,
                Key = key,
                UploadId = session.StorageUploadId,
                PartETags = [.. parts.OrderBy(part => part.Number).Select(part => new PartETag(part.Number, part.ETag))],
            });
        }
        catch (AmazonS3Exception failure) when (failure.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound)
        {
            // A part is missing, too small or its ETag does not match what storage holds.
            throw new BusinessException(UploadErrors.Incomplete);
        }

        var stored = await client.GetObjectMetadataAsync(bucket, key);
        return stored.ContentLength;
    }

    public async Task AbortAsync(UploadSession session)
    {
        var (bucket, key, aws) = Locate(session);
        using var client = await clients.GetAmazonS3Client(aws);
        try
        {
            await client.AbortMultipartUploadAsync(bucket, key, session.StorageUploadId);
        }
        catch (AmazonS3Exception failure) when (failure.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Completed or aborted before; nothing is left to free.
        }
    }
}

/// <summary>
/// ABP's factory knows AWS regions only. With Storage:S3:Endpoint set it talks to that S3-compatible service instead, such
/// as MinIO or Cloudflare R2, with path-style addresses and checksums only where the S3 API requires them.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IAmazonS3ClientFactory))]
public class EndpointAmazonS3ClientFactory(IConfiguration configuration) : IAmazonS3ClientFactory, ITransientDependency
{
    public Task<AmazonS3Client> GetAmazonS3Client(AwsBlobProviderConfiguration aws)
    {
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var credentials = new BasicAWSCredentials(aws.AccessKeyId, aws.SecretAccessKey);
        var config = new AmazonS3Config
        {
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        if (string.IsNullOrEmpty(storage.S3.Endpoint))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(aws.Region);
        }
        else
        {
            config.ServiceURL = storage.S3.Endpoint;
            config.ForcePathStyle = true;
            config.AuthenticationRegion = string.IsNullOrEmpty(storage.S3.Region) ? "us-east-1" : storage.S3.Region;
        }

        return Task.FromResult(new AmazonS3Client(credentials, config));
    }
}

[Authorize]
public class UploadAppService(
    IRepository<UploadSession, Guid> sessions,
    IRepository<Issue, Guid> issues,
    IRepository<RichTextFile, Guid> richTextFiles,
    IUploadStorage storage,
    IProjectAccess projectAccess,
    AttachmentRecorder attachments,
    RichTextFileStore richTextStore,
    IConfiguration configuration,
    IClock clock)
    : ApplicationService, IUploadAppService
{
    /// <summary>Well under the 100 MB a request may carry through Cloudflare, and above the 5 MB S3 wants per part.</summary>
    private const int DefaultPartSizeKb = 16 * 1024;

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromHours(1);

    public async Task<UploadSessionDto> StartAsync(StartUploadInput input)
    {
        var projectId = await CheckTargetAsync(input.Purpose, input.IssueId, input.ProjectId);
        AttachmentRecorder.CheckSize(input.Size, configuration);

        var partSize = configuration.GetValue("Issues:Uploads:PartSizeKb", DefaultPartSizeKb) * 1024L;
        var now = clock.Now;
        var session = new UploadSession(
            GuidGenerator.Create(),
            input.Purpose,
            projectId,
            input.Purpose == UploadPurpose.Attachment ? input.IssueId : null,
            GuidGenerator.Create(),
            Path.GetFileName(input.FileName.Trim()) is { Length: > 0 } name ? name : "file",
            string.IsNullOrWhiteSpace(input.ContentType) ? "application/octet-stream" : input.ContentType.Trim(),
            input.Size,
            partSize,
            CurrentUser.GetId(),
            now,
            now + SessionLifetime);
        session.StorageUploadId = await storage.BeginAsync(session);
        await sessions.InsertAsync(session, autoSave: true);
        return ToDto(session);
    }

    public async Task<UploadedPartDto> UploadPartAsync(Guid id, int number, IRemoteStreamContent content)
    {
        var session = await GetOwnAsync(id);
        if (storage.Direct || number < 1 || number > session.PartCount)
        {
            throw new BusinessException(UploadErrors.PartInvalid);
        }

        await using var stream = content.GetStream();
        return new UploadedPartDto { Number = number, ETag = await storage.WritePartAsync(session, number, stream) };
    }

    public async Task<UploadSessionDto> RenewAsync(Guid id) => ToDto(await GetOwnAsync(id));

    public async Task<UploadResultDto> CompleteAsync(Guid id, CompleteUploadInput input)
    {
        var session = await GetOwnAsync(id);
        var numbers = input.Parts.Select(part => part.Number).Order().ToList();
        if (!numbers.SequenceEqual(Enumerable.Range(1, session.PartCount)))
        {
            throw new BusinessException(UploadErrors.Incomplete);
        }

        // Access may have changed while the file went up.
        await CheckTargetAsync(session.Purpose, session.IssueId, session.ProjectId);
        var size = await storage.CompleteAsync(session, input.Parts);
        await sessions.DeleteAsync(session);
        if (size != session.Size)
        {
            await attachments.DeleteBlobAsync(session.StorageKey);
            throw new BusinessException(UploadErrors.Incomplete);
        }

        if (session.Purpose == UploadPurpose.Attachment)
        {
            return new UploadResultDto
            {
                Attachment = await attachments.RecordAsync(session.IssueId!.Value, session.FileId, session.FileName, size, session.ContentType, session.StorageKey),
            };
        }

        var file = new RichTextFile(session.FileId, session.ProjectId, session.FileName, session.ContentType, size, clock.Now);
        await richTextFiles.InsertAsync(file, autoSave: true);
        return new UploadResultDto
        {
            RichTextFile = new RichTextFileDto
            {
                Id = file.Id,
                Url = richTextStore.SignedUrl(file.Id),
                FileName = file.FileName,
                ContentType = file.ContentType,
                Size = file.Size,
            },
        };
    }

    public async Task AbortAsync(Guid id)
    {
        var session = await sessions.FindAsync(id);
        if (session is null || session.CreatorId != CurrentUser.GetId())
        {
            return;
        }

        await storage.AbortAsync(session);
        await sessions.DeleteAsync(session);
    }

    /// <summary>Returns the space the file belongs to, after checking the person may add files there.</summary>
    private async Task<Guid> CheckTargetAsync(UploadPurpose purpose, Guid? issueId, Guid? projectId)
    {
        Guid target;
        if (purpose == UploadPurpose.Attachment && issueId is { } issue)
        {
            target = (await issues.GetAsync(issue)).ProjectId;
        }
        else if (purpose == UploadPurpose.RichText && projectId is { } project)
        {
            target = project;
        }
        else
        {
            throw new BusinessException(UploadErrors.TargetMissing);
        }

        await projectAccess.CheckAsync(target, ProjectPermissions.EditIssues);
        return target;
    }

    /// <summary>Sessions belong to whoever started them; to anyone else they do not exist.</summary>
    private async Task<UploadSession> GetOwnAsync(Guid id)
    {
        var session = await sessions.FindAsync(id);
        if (session is null || session.CreatorId != CurrentUser.GetId())
        {
            throw new EntityNotFoundException(typeof(UploadSession), id);
        }

        if (session.ExpiresAt <= clock.Now)
        {
            throw new BusinessException(UploadErrors.Expired);
        }

        return session;
    }

    private UploadSessionDto ToDto(UploadSession session)
    {
        var urlsExpire = storage.Direct ? Min(clock.Now + UrlLifetime, session.ExpiresAt) : session.ExpiresAt;
        return new UploadSessionDto
        {
            Id = session.Id,
            PartSize = session.PartSize,
            ExpiresAt = urlsExpire,
            Parts = [.. Enumerable.Range(1, session.PartCount).Select(number => new UploadPartTargetDto
            {
                Number = number,
                Url = storage.PartUrl(session, number, urlsExpire),
            })],
        };
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}

/// <summary>Records a stored blob as an attachment; shared by single and part uploads.</summary>
public class AttachmentRecorder(
    IRepository<Attachment, Guid> attachments,
    IRepository<IssueActivity, Guid> activities,
    IBlobContainer<AttachmentContainer> container,
    IUserDirectory userDirectory,
    ICurrentActor currentActor,
    IGuidGenerator guids,
    IClock clock) : ITransientDependency
{
    /// <summary>Two gigabytes: parts carry files of that size past proxies that cap each request.</summary>
    public const int DefaultMaxFileSizeMb = 2048;

    public static void CheckSize(long? size, IConfiguration configuration)
    {
        var maxMb = configuration.GetValue("Issues:Attachments:MaxFileSizeMb", DefaultMaxFileSizeMb);
        if (size is null or <= 0 || size > maxMb * 1024L * 1024L)
        {
            throw new BusinessException(IssueErrors.AttachmentTooLarge).WithData("maxMb", maxMb);
        }
    }

    public async Task<AttachmentDto> RecordAsync(Guid issueId, Guid id, string fileName, long size, string contentType, string storageKey)
    {
        var attachment = new Attachment(id, issueId, fileName, size, contentType, storageKey);
        await attachments.InsertAsync(attachment, autoSave: true);
        await activities.InsertAsync(new IssueActivity(
            guids.Create(), issueId, currentActor.Actor, IssueFieldKeys.Attachment, null, fileName, clock.Now));
        return (await ToDtosAsync([attachment])).Single();
    }

    public Task DeleteBlobAsync(string storageKey) => container.DeleteAsync(storageKey);

    public async Task<List<AttachmentDto>> ToDtosAsync(IEnumerable<Attachment> list)
    {
        var items = list.ToList();
        var users = await userDirectory.GetByIdsAsync(items.Where(i => i.CreatorId.HasValue).Select(i => i.CreatorId!.Value));
        return [.. items.Select(item => new AttachmentDto
        {
            Id = item.Id,
            FileName = item.FileName,
            Size = item.Size,
            ContentType = item.ContentType,
            UploadedBy = item.CreatorId is { } creator ? users.GetValueOrDefault(creator) : null,
            CreatedAt = item.CreationTime,
        })];
    }
}

/// <summary>Frees what uploads that were never completed hold in storage.</summary>
public class UploadCleanupWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int HourMilliseconds = 60 * 60 * 1000;

    public UploadCleanupWorker(AbpAsyncTimer timer, IServiceScopeFactory scopeFactory)
        : base(timer, scopeFactory)
    {
        timer.Period = HourMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        await using var handle = await services.GetRequiredService<IAbpDistributedLock>().TryAcquireAsync(nameof(UploadCleanupWorker));
        if (handle is not null)
        {
            await CleanAsync(services, services.GetRequiredService<IClock>().Now);
        }
    }

    public static async Task CleanAsync(IServiceProvider services, DateTime now)
    {
        var sessions = services.GetRequiredService<IRepository<UploadSession, Guid>>();
        var storage = services.GetRequiredService<IUploadStorage>();
        var unitOfWork = services.GetRequiredService<IUnitOfWorkManager>();
        List<UploadSession> expired;
        using (var uow = unitOfWork.Begin(requiresNew: true))
        {
            expired = await sessions.GetListAsync(session => session.ExpiresAt <= now);
            await uow.CompleteAsync();
        }

        foreach (var session in expired)
        {
            try
            {
                using var uow = unitOfWork.Begin(requiresNew: true);
                await storage.AbortAsync(session);
                await sessions.DeleteAsync(session.Id);
                await uow.CompleteAsync();
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                services.GetRequiredService<ILogger<UploadCleanupWorker>>().LogWarning(failure, "Upload {UploadId} could not be cleaned up yet", session.Id);
            }
        }
    }
}
