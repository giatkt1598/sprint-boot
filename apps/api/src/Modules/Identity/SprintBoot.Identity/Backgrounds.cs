using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace SprintBoot.Identity;

[BlobContainerName("backgrounds")]
public class BackgroundContainer;

/// <summary>The page background a person chose. Like avatars, each upload gets a fresh key, so its address can be cached for good.</summary>
public static class UserBackgrounds
{
    public const string KeyProperty = "BackgroundKey";
    public const string TypeProperty = "BackgroundType";
    public const long MaxBytes = 4 * 1024 * 1024;

    public static string? Url(IdentityUser user) =>
        user.GetProperty<string?>(KeyProperty) is { Length: > 0 } key ? $"/api/v1/background/{user.Id}/{key}" : null;
}

public class BackgroundStore(IBlobContainer<BackgroundContainer> container, IdentityUserManager users) : ITransientDependency
{
    public async Task SaveAsync(IdentityUser user, IRemoteStreamContent file)
    {
        if (file.ContentLength is null or 0 || file.ContentLength > UserBackgrounds.MaxBytes)
        {
            throw Invalid();
        }

        using var buffer = new MemoryStream();
        await using (var stream = file.GetStream())
        {
            await stream.CopyToAsync(buffer);
        }

        var type = UserAvatars.Sniff(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)));
        if (type is null || buffer.Length > UserBackgrounds.MaxBytes)
        {
            throw Invalid();
        }

        var previous = user.GetProperty<string?>(UserBackgrounds.KeyProperty);
        var key = Guid.NewGuid().ToString("N");
        buffer.Position = 0;
        await container.SaveAsync(BlobName(user.Id, key), buffer);
        user.SetProperty(UserBackgrounds.KeyProperty, key);
        user.SetProperty(UserBackgrounds.TypeProperty, type);
        (await users.UpdateAsync(user)).CheckErrors();
        await DeleteBlobAsync(user.Id, previous);
    }

    public async Task RemoveAsync(IdentityUser user)
    {
        var previous = user.GetProperty<string?>(UserBackgrounds.KeyProperty);
        user.RemoveProperty(UserBackgrounds.KeyProperty);
        user.RemoveProperty(UserBackgrounds.TypeProperty);
        (await users.UpdateAsync(user)).CheckErrors();
        await DeleteBlobAsync(user.Id, previous);
    }

    public async Task<(Stream Content, string Type)?> FindAsync(Guid userId, string key)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || user.GetProperty<string?>(UserBackgrounds.KeyProperty) != key)
        {
            return null;
        }

        var stream = await container.GetOrNullAsync(BlobName(userId, key));
        return stream is null ? null : (stream, user.GetProperty(UserBackgrounds.TypeProperty, "image/png")!);
    }

    private static BusinessException Invalid() =>
        new BusinessException(AuthErrors.BackgroundInvalid).WithData("maxMb", UserBackgrounds.MaxBytes / 1024 / 1024);

    private async Task DeleteBlobAsync(Guid userId, string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            await container.DeleteAsync(BlobName(userId, key));
        }
    }

    private static string BlobName(Guid userId, string key) => $"{userId:N}/{key}";
}

/// <summary>Anonymous because a CSS background cannot send a token; the key in the address is unguessable.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/background")]
[AllowAnonymous]
public class BackgroundController(BackgroundStore store) : ControllerBase
{
    [HttpGet("{userId:guid}/{key}")]
    public async Task<IActionResult> GetAsync(Guid userId, string key)
    {
        if (await store.FindAsync(userId, key) is not { } image)
        {
            return NotFound();
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(image.Content, image.Type);
    }
}
