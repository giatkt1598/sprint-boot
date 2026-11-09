using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace SprintBoot.Tickets;

public interface ITicketAppService : IApplicationService
{
    Task<List<TicketTypeDto>> GetTypesAsync(Guid projectId);

    Task<List<PriorityDto>> GetPrioritiesAsync();

    Task<TicketDto> GetByKeyAsync(string key);

    Task<TicketDto> CreateAsync(CreateTicketInput input);

    Task<TicketDto> UpdateAsync(Guid id, UpdateTicketInput input);

    Task DeleteAsync(Guid id);

    Task<TicketSearchResultDto> GetSearchAsync(TicketSearchInput input);

    Task<List<string>> GetLabelsAsync(Guid projectId);

    Task<List<EpicRowDto>> GetEpicsAsync(Guid projectId);

    Task<List<TicketActivityDto>> GetActivityAsync(Guid id);

    Task<PagedResultDto<DeletedTicketDto>> GetDeletedListAsync(PagedResultRequestDto input);

    Task RestoreAsync(Guid id);
}

public interface IWatcherAppService : IApplicationService
{
    Task<WatchersDto> GetAsync(Guid ticketId);

    Task<WatchersDto> WatchAsync(Guid ticketId);

    Task<WatchersDto> UnwatchAsync(Guid ticketId);
}

public interface ICommentAppService : IApplicationService
{
    Task<List<CommentDto>> GetListAsync(Guid ticketId);

    Task<CommentDto> CreateAsync(CreateCommentInput input);

    Task<CommentDto> UpdateAsync(Guid id, UpdateCommentInput input);

    Task DeleteAsync(Guid id);

    Task<CommentDto> ToggleReactionAsync(Guid id, ToggleReactionInput input);
}

public interface IAttachmentAppService : IApplicationService
{
    Task<List<AttachmentDto>> GetListAsync(Guid ticketId);

    Task<AttachmentDto> UploadAsync(Guid ticketId, IRemoteStreamContent file);

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
    Task<WorkLogListDto> GetListAsync(Guid ticketId);

    Task<WorkLogDto> CreateAsync(CreateWorkLogInput input);

    Task<WorkLogDto> UpdateAsync(Guid id, UpdateWorkLogInput input);

    Task DeleteAsync(Guid id);
}
