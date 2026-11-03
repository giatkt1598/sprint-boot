using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SprintBoot.Platform;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Projects;

[BlobContainerName("space-logos")]
public class SpaceLogoContainer;

/// <summary>
/// A space's logo, under a fresh key per upload so its address can be cached for good. The key is
/// the permission: img tags cannot send a token, and a logo says no more than the space name does.
/// </summary>
public class SpaceLogos(IBlobContainer<SpaceLogoContainer> container) : ITransientDependency
{
    private const string KeyProperty = "LogoKey";
    private const string TypeProperty = "LogoType";
    public const long MaxBytes = 512 * 1024;

    public static string? Url(Project project) =>
        project.GetProperty<string?>(KeyProperty) is { Length: > 0 } key ? $"/api/v1/space-logo/{project.Id}/{key}" : null;

    public async Task SaveAsync(Project project, IRemoteStreamContent file)
    {
        if (file.ContentLength is null or 0 || file.ContentLength > MaxBytes)
        {
            throw new BusinessException(ProjectErrors.LogoInvalid);
        }

        using var buffer = new MemoryStream();
        await using (var stream = file.GetStream())
        {
            await stream.CopyToAsync(buffer);
        }

        var type = ImageFiles.Sniff(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)));
        if (type is null || buffer.Length > MaxBytes)
        {
            throw new BusinessException(ProjectErrors.LogoInvalid);
        }

        var previous = project.GetProperty<string?>(KeyProperty);
        var key = Guid.NewGuid().ToString("N");
        buffer.Position = 0;
        await container.SaveAsync(BlobName(project.Id, key), buffer);
        project.SetProperty(KeyProperty, key);
        project.SetProperty(TypeProperty, type);
        await DeleteBlobAsync(project.Id, previous);
    }

    public async Task RemoveAsync(Project project)
    {
        var previous = project.GetProperty<string?>(KeyProperty);
        project.RemoveProperty(KeyProperty);
        project.RemoveProperty(TypeProperty);
        await DeleteBlobAsync(project.Id, previous);
    }

    public async Task<(Stream Content, string Type)?> FindAsync(Project project, string key)
    {
        if (project.GetProperty<string?>(KeyProperty) != key)
        {
            return null;
        }

        var stream = await container.GetOrNullAsync(BlobName(project.Id, key));
        return stream is null ? null : (stream, project.GetProperty(TypeProperty, "image/png")!);
    }

    private async Task DeleteBlobAsync(Guid projectId, string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            await container.DeleteAsync(BlobName(projectId, key));
        }
    }

    private static string BlobName(Guid projectId, string key) => $"{projectId:N}/{key}";
}

[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/space-logo")]
[AllowAnonymous]
public class SpaceLogoController(IRepository<Project, Guid> projects, SpaceLogos logos) : ControllerBase
{
    [HttpGet("{projectId:guid}/{key}")]
    public async Task<IActionResult> GetAsync(Guid projectId, string key)
    {
        var project = await projects.FindAsync(projectId);
        if (project is null || await logos.FindAsync(project, key) is not { } logo)
        {
            return NotFound();
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(logo.Content, logo.Type);
    }
}
