using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace SprintBoot.Identity;

[BlobContainerName("avatars")]
public class AvatarContainer;

/// <summary>
/// A user's picture lives under a fresh key on every upload, so its address never serves stale
/// bytes and can be cached for good; the address alone is the permission to see it.
/// </summary>
public static class UserAvatars
{
    public const string KeyProperty = "AvatarKey";
    public const string TypeProperty = "AvatarType";
    public const long MaxBytes = 2 * 1024 * 1024;

    public static readonly string[] Types = ["image/png", "image/jpeg", "image/webp", "image/gif"];

    public static string? Url(IdentityUser user) =>
        user.GetProperty<string?>(KeyProperty) is { Length: > 0 } key ? $"/api/v1/avatar/{user.Id}/{key}" : null;

    /// <summary>What the bytes are, whatever the upload claimed, so nothing but an image is ever served.</summary>
    public static string? Sniff(ReadOnlySpan<byte> head) => head switch
    {
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };
}

public class AvatarStore(IBlobContainer<AvatarContainer> container, IdentityUserManager users) : ITransientDependency
{
    public async Task SaveAsync(IdentityUser user, IRemoteStreamContent file)
    {
        if (file.ContentLength is null or 0 || file.ContentLength > UserAvatars.MaxBytes)
        {
            throw new BusinessException(AuthErrors.AvatarInvalid).WithData("maxMb", UserAvatars.MaxBytes / 1024 / 1024);
        }

        using var buffer = new MemoryStream();
        await using (var stream = file.GetStream())
        {
            await stream.CopyToAsync(buffer);
        }

        var type = UserAvatars.Sniff(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)));
        if (type is null || buffer.Length > UserAvatars.MaxBytes)
        {
            throw new BusinessException(AuthErrors.AvatarInvalid).WithData("maxMb", UserAvatars.MaxBytes / 1024 / 1024);
        }

        var previous = user.GetProperty<string?>(UserAvatars.KeyProperty);
        var key = Guid.NewGuid().ToString("N");
        buffer.Position = 0;
        await container.SaveAsync(BlobName(user.Id, key), buffer);
        user.SetProperty(UserAvatars.KeyProperty, key);
        user.SetProperty(UserAvatars.TypeProperty, type);
        (await users.UpdateAsync(user)).CheckErrors();
        await DeleteBlobAsync(user.Id, previous);
    }

    public async Task RemoveAsync(IdentityUser user)
    {
        var previous = user.GetProperty<string?>(UserAvatars.KeyProperty);
        user.RemoveProperty(UserAvatars.KeyProperty);
        user.RemoveProperty(UserAvatars.TypeProperty);
        (await users.UpdateAsync(user)).CheckErrors();
        await DeleteBlobAsync(user.Id, previous);
    }

    public async Task<(Stream Content, string Type)?> FindAsync(Guid userId, string key)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || user.GetProperty<string?>(UserAvatars.KeyProperty) != key)
        {
            return null;
        }

        var stream = await container.GetOrNullAsync(BlobName(userId, key));
        return stream is null ? null : (stream, user.GetProperty(UserAvatars.TypeProperty, "image/png")!);
    }

    private async Task DeleteBlobAsync(Guid userId, string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            await container.DeleteAsync(BlobName(userId, key));
        }
    }

    private static string BlobName(Guid userId, string key) => $"{userId:N}/{key}";
}

/// <summary>Anonymous because img tags cannot send a token; the key in the address is unguessable.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/avatar")]
[AllowAnonymous]
public class AvatarController(AvatarStore store) : ControllerBase
{
    [HttpGet("{userId:guid}/{key}")]
    public async Task<IActionResult> GetAsync(Guid userId, string key)
    {
        if (await store.FindAsync(userId, key) is not { } avatar)
        {
            return NotFound();
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(avatar.Content, avatar.Type);
    }
}
