using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace SprintBoot.Notifications;

public static class NotificationEventTypes
{
    public const string IssueCreated = "issue.created";
    public const string IssueAssigned = "issue.assigned";
    public const string IssueUpdated = "issue.updated";
    public const string CommentAdded = "comment.added";
    public const string Mentioned = "mentioned";
    public const string CommentReply = "comment.reply";
    public const string CommentReaction = "comment.reaction";
    public const string IssueTransitioned = "issue.transitioned";
    public const string WorkflowNotification = "workflow.notification";
    public const string SprintStarted = "sprint.started";
    public const string SprintCompleted = "sprint.completed";
    public const string MemberAdded = "project.member-added";

    public static readonly string[] All =
    [
        IssueCreated, IssueAssigned, IssueUpdated, CommentAdded, Mentioned, CommentReply, CommentReaction,
        IssueTransitioned, WorkflowNotification, SprintStarted, SprintCompleted, MemberAdded,
    ];
}

public enum RecipientType
{
    Assignee = 0,
    Reporter = 1,
    Watcher = 2,
    Mentioned = 3,
    ProjectRole = 4,
    User = 5,
}

public class NotificationDto
{
    public required Guid Id { get; set; }

    public required string EventType { get; set; }

    public required string? IssueKey { get; set; }

    public required string? Title { get; set; }

    public required string? ActorName { get; set; }

    public required string? Detail { get; set; }

    public required int MergedCount { get; set; }

    public required DateTime? ReadAt { get; set; }

    public required DateTime CreatedAt { get; set; }
}

public class NotificationListInput : PagedResultRequestDto
{
    public bool UnreadOnly { get; set; }
}

public class NotificationPreferenceDto
{
    public required string EventType { get; set; }

    public required bool Enabled { get; set; }
}

public class SavePreferencesInput
{
    public required List<NotificationPreferenceDto> Items { get; set; }
}

public class NotificationRuleDto
{
    public required string EventType { get; set; }

    public required RecipientType RecipientType { get; set; }

    /// <summary>Role name for ProjectRole, user id for User.</summary>
    public string? RecipientValue { get; set; }
}

public class NotificationSchemeDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required string? Description { get; set; }

    public required bool IsDefault { get; set; }

    public required int SpaceCount { get; set; }

    public required List<NotificationRuleDto> Rules { get; set; }
}

public class SaveSchemeInput
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string Name { get; set; }

    [StringLength(512)]
    public string? Description { get; set; }

    public required List<NotificationRuleDto> Rules { get; set; }
}

public class ProjectSchemeDto
{
    public required Guid SchemeId { get; set; }

    public required string Name { get; set; }
}

public class SetProjectSchemeInput
{
    public required Guid ProjectId { get; set; }

    public required Guid SchemeId { get; set; }
}

public interface INotificationAppService : IApplicationService
{
    Task<PagedResultDto<NotificationDto>> GetListAsync(NotificationListInput input);

    Task<int> GetUnreadCountAsync();

    Task MarkReadAsync(Guid id);

    Task MarkAllReadAsync();

    Task<List<NotificationPreferenceDto>> GetPreferencesAsync();

    Task SavePreferencesAsync(SavePreferencesInput input);
}

public interface INotificationSchemeAppService : IApplicationService
{
    Task<List<NotificationSchemeDto>> GetListAsync();

    Task<NotificationSchemeDto> GetAsync(Guid id);

    Task<NotificationSchemeDto> CreateAsync(SaveSchemeInput input);

    Task<NotificationSchemeDto> UpdateAsync(Guid id, SaveSchemeInput input);

    Task DeleteAsync(Guid id);

    Task<ProjectSchemeDto> GetForProjectAsync(Guid projectId);

    Task<ProjectSchemeDto> SetForProjectAsync(SetProjectSchemeInput input);

    /// <summary>Names only, so Project Admins can choose a scheme without managing them.</summary>
    Task<List<ProjectSchemeDto>> GetChoicesAsync();
}

public static class NotificationErrors
{
    public const string UnknownEventType = "SprintBoot:UnknownNotificationEvent";
    public const string DefaultSchemeLocked = "SprintBoot:DefaultSchemeLocked";
    public const string SchemeInUse = "SprintBoot:NotificationSchemeInUse";
}
