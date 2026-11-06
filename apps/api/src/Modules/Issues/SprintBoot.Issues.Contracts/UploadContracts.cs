using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace SprintBoot.Issues;

/// <summary>What an upload becomes once it completes.</summary>
public enum UploadPurpose
{
    /// <summary>An attachment of an issue.</summary>
    Attachment = 0,

    /// <summary>A file placed in rich text of a space.</summary>
    RichText = 1,
}

public class StartUploadInput
{
    public UploadPurpose Purpose { get; set; }

    /// <summary>Attachments only.</summary>
    public Guid? IssueId { get; set; }

    /// <summary>Rich text only.</summary>
    public Guid? ProjectId { get; set; }

    [Required]
    [StringLength(UploadConsts.MaxFileNameLength)]
    public required string FileName { get; set; }

    [StringLength(UploadConsts.MaxContentTypeLength)]
    public string? ContentType { get; set; }

    [Range(1, long.MaxValue)]
    public long Size { get; set; }
}

/// <summary>
/// A file sent in parts, so no request is larger than a proxy such as Cloudflare accepts. Parts with a URL go straight to
/// object storage; parts without one go to the part endpoint.
/// </summary>
public class UploadSessionDto
{
    public required Guid Id { get; set; }

    public required long PartSize { get; set; }

    public required DateTime ExpiresAt { get; set; }

    public required List<UploadPartTargetDto> Parts { get; set; }
}

public class UploadPartTargetDto
{
    /// <summary>From 1, as object storage counts parts.</summary>
    public required int Number { get; set; }

    /// <summary>A presigned PUT, valid until the session's ExpiresAt; null when the part goes through the API.</summary>
    public string? Url { get; set; }
}

public class UploadedPartDto
{
    public required int Number { get; set; }

    /// <summary>The ETag storage answered the part with; object storage needs them all to join the parts.</summary>
    [Required]
    public required string ETag { get; set; }
}

public class CompleteUploadInput
{
    [Required]
    public required List<UploadedPartDto> Parts { get; set; }
}

/// <summary>Exactly one of them is set, as the session's purpose says.</summary>
public class UploadResultDto
{
    public AttachmentDto? Attachment { get; set; }

    public RichTextFileDto? RichTextFile { get; set; }
}

public static class UploadConsts
{
    public const int MaxFileNameLength = 255;
    public const int MaxContentTypeLength = 128;
}

public static class UploadErrors
{
    public const string TargetMissing = "SprintBoot:UploadTargetMissing";
    public const string Expired = "SprintBoot:UploadExpired";
    public const string PartInvalid = "SprintBoot:UploadPartInvalid";
    public const string Incomplete = "SprintBoot:UploadIncomplete";
}

public interface IUploadAppService : IApplicationService
{
    Task<UploadSessionDto> StartAsync(StartUploadInput input);

    /// <summary>Only for sessions whose parts carry no URL.</summary>
    Task<UploadedPartDto> UploadPartAsync(Guid id, int number, IRemoteStreamContent content);

    /// <summary>New presigned URLs and a later expiry, to retry after the old ones ran out.</summary>
    Task<UploadSessionDto> RenewAsync(Guid id);

    Task<UploadResultDto> CompleteAsync(Guid id, CompleteUploadInput input);

    Task AbortAsync(Guid id);
}
