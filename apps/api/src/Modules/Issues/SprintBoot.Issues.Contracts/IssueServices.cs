using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace SprintBoot.Issues;

public interface IIssueAppService : IApplicationService
{
    Task<List<IssueTypeDto>> GetTypesAsync(Guid projectId);

    Task<List<PriorityDto>> GetPrioritiesAsync();

    Task<IssueDto> GetByKeyAsync(string key);

    Task<IssueDto> CreateAsync(CreateIssueInput input);

    Task<IssueDto> UpdateAsync(Guid id, UpdateIssueInput input);

    Task DeleteAsync(Guid id);

    Task<IssueSearchResultDto> GetSearchAsync(IssueSearchInput input);

    Task<List<string>> GetLabelsAsync(Guid projectId);

    Task<List<EpicRowDto>> GetEpicsAsync(Guid projectId);

    Task<List<IssueActivityDto>> GetActivityAsync(Guid id);

    Task<PagedResultDto<DeletedIssueDto>> GetDeletedListAsync(PagedResultRequestDto input);

    Task RestoreAsync(Guid id);
}

public interface IWatcherAppService : IApplicationService
{
    Task<WatchersDto> GetAsync(Guid issueId);

    Task<WatchersDto> WatchAsync(Guid issueId);

    Task<WatchersDto> UnwatchAsync(Guid issueId);
}

public interface ICommentAppService : IApplicationService
{
    Task<List<CommentDto>> GetListAsync(Guid issueId);

    Task<CommentDto> CreateAsync(CreateCommentInput input);

    Task<CommentDto> UpdateAsync(Guid id, UpdateCommentInput input);

    Task DeleteAsync(Guid id);

    Task<CommentDto> ToggleReactionAsync(Guid id, ToggleReactionInput input);
}

public interface IAttachmentAppService : IApplicationService
{
    Task<List<AttachmentDto>> GetListAsync(Guid issueId);

    Task<AttachmentDto> UploadAsync(Guid issueId, IRemoteStreamContent file);

    Task<IRemoteStreamContent> GetDownloadAsync(Guid id);

    Task DeleteAsync(Guid id);
}

public class RichTextFileDto
{
    public required Guid Id { get; set; }

    /// <summary>Signed for a while, because img and video tags cannot send a token.</summary>
    public required string Url { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public required long Size { get; set; }
}

public interface IRichTextFileAppService : IApplicationService
{
    Task<RichTextFileDto> UploadAsync(Guid projectId, IRemoteStreamContent file);
}

public interface IWorkLogAppService : IApplicationService
{
    Task<WorkLogListDto> GetListAsync(Guid issueId);

    Task<WorkLogDto> CreateAsync(CreateWorkLogInput input);

    Task<WorkLogDto> UpdateAsync(Guid id, UpdateWorkLogInput input);

    Task DeleteAsync(Guid id);
}
