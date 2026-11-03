using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Identity;

[BlobContainerName("provider-logos")]
public class ProviderLogoContainer;

/// <summary>
/// The look of an OIDC provider's sign-in button. Stored as extra properties, and the logo under a
/// fresh key per upload, so its address can be cached for good on the anonymous sign-in page.
/// </summary>
public class ProviderLogos(IBlobContainer<ProviderLogoContainer> container) : ITransientDependency
{
    private const string KeyProperty = "LogoKey";
    private const string TypeProperty = "LogoType";
    private const string ColorProperty = "ButtonColor";
    private const long MaxBytes = 256 * 1024;

    public static string? Url(ExternalProviderConfig provider) =>
        provider.GetProperty<string?>(KeyProperty) is { Length: > 0 } key ? $"/api/v1/sso-logo/{provider.Id}/{key}" : null;

    public static string? Color(ExternalProviderConfig provider) => provider.GetProperty<string?>(ColorProperty);

    public static void SetColor(ExternalProviderConfig provider, string? color)
    {
        if (string.IsNullOrWhiteSpace(color) || provider.Type != ExternalProviderType.Oidc)
        {
            provider.RemoveProperty(ColorProperty);
        }
        else
        {
            provider.SetProperty(ColorProperty, color.ToUpperInvariant());
        }
    }

    public async Task SaveAsync(ExternalProviderConfig provider, IRemoteStreamContent file)
    {
        if (provider.Type != ExternalProviderType.Oidc || file.ContentLength is null or 0 || file.ContentLength > MaxBytes)
        {
            throw new BusinessException(SsoErrors.LogoInvalid);
        }

        using var buffer = new MemoryStream();
        await using (var stream = file.GetStream())
        {
            await stream.CopyToAsync(buffer);
        }

        // SVG is refused on purpose: it can carry script, and this is served on our origin.
        var type = UserAvatars.Sniff(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)));
        if (type is null || buffer.Length > MaxBytes)
        {
            throw new BusinessException(SsoErrors.LogoInvalid);
        }

        var previous = provider.GetProperty<string?>(KeyProperty);
        var key = Guid.NewGuid().ToString("N");
        buffer.Position = 0;
        await container.SaveAsync(BlobName(provider.Id, key), buffer);
        provider.SetProperty(KeyProperty, key);
        provider.SetProperty(TypeProperty, type);
        await DeleteBlobAsync(provider.Id, previous);
    }

    public async Task RemoveAsync(ExternalProviderConfig provider)
    {
        var previous = provider.GetProperty<string?>(KeyProperty);
        provider.RemoveProperty(KeyProperty);
        provider.RemoveProperty(TypeProperty);
        await DeleteBlobAsync(provider.Id, previous);
    }

    public async Task<(Stream Content, string Type)?> FindAsync(ExternalProviderConfig provider, string key)
    {
        if (provider.GetProperty<string?>(KeyProperty) != key)
        {
            return null;
        }

        var stream = await container.GetOrNullAsync(BlobName(provider.Id, key));
        return stream is null ? null : (stream, provider.GetProperty(TypeProperty, "image/png")!);
    }

    private async Task DeleteBlobAsync(Guid providerId, string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            await container.DeleteAsync(BlobName(providerId, key));
        }
    }

    private static string BlobName(Guid providerId, string key) => $"{providerId:N}/{key}";
}

/// <summary>Anonymous: the sign-in page shows it before anyone is signed in.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/sso-logo")]
[AllowAnonymous]
public class ProviderLogoController(IRepository<ExternalProviderConfig, Guid> providers, ProviderLogos logos) : ControllerBase
{
    [HttpGet("{providerId:guid}/{key}")]
    public async Task<IActionResult> GetAsync(Guid providerId, string key)
    {
        var provider = await providers.FindAsync(providerId);
        if (provider is null || await logos.FindAsync(provider, key) is not { } logo)
        {
            return NotFound();
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(logo.Content, logo.Type);
    }
}
