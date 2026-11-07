using Microsoft.AspNetCore.Authorization;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace SprintBoot.Issues;

/// <summary>A target a space works toward, reported on by its owner. Deleting one keeps it for audit.</summary>
public class Goal : FullAuditedAggregateRoot<Guid>
{
    public Guid ProjectId { get; private set; }

    public string Name { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    public Guid? OwnerId { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? TargetDate { get; set; }

    public GoalStatus Status { get; set; }

    public int Progress { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    protected Goal()
    {
    }

    public Goal(Guid id, Guid projectId)
        : base(id)
    {
        ProjectId = projectId;
    }
}

/// <summary>One report on a goal, kept as the history of how it went.</summary>
public class GoalUpdate : Entity<Guid>
{
    public Guid GoalId { get; private set; }

    public Guid AuthorId { get; private set; }

    public GoalStatus Status { get; private set; }

    public int Progress { get; private set; }

    public string? Note { get; private set; }

    public DateTime CreatedAt { get; private set; }

    protected GoalUpdate()
    {
    }

    public GoalUpdate(Guid id, Guid goalId, Guid authorId, GoalStatus status, int progress, string? note, DateTime createdAt)
        : base(id)
    {
        GoalId = goalId;
        AuthorId = authorId;
        Status = status;
        Progress = progress;
        Note = note;
        CreatedAt = createdAt;
    }
}

[Authorize]
public class GoalAppService(
    IRepository<Goal, Guid> goals,
    IRepository<GoalUpdate, Guid> updates,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    RichTextSanitizer sanitizer,
    IClock clock)
    : ApplicationService, IGoalAppService
{
    public async Task<List<GoalDto>> GetListAsync(Guid projectId, bool archived = false)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        var list = await goals.GetListAsync(goal => goal.ProjectId == projectId && (goal.ArchivedAt != null) == archived);
        // Soonest target first, goals with no target last; ties by name.
        var ordered = list
            .OrderBy(goal => goal.TargetDate is null)
            .ThenBy(goal => goal.TargetDate)
            .ThenBy(goal => goal.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return await ToDtosAsync(projectId, ordered);
    }

    public async Task<GoalDto> CreateAsync(Guid projectId, SaveGoalInput input)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.Manage);
        var goal = new Goal(GuidGenerator.Create(), projectId) { Status = GoalStatus.Pending };
        await ApplyAsync(goal, input);
        await goals.InsertAsync(goal, autoSave: true);
        return (await ToDtosAsync(projectId, [goal])).Single();
    }

    public async Task<GoalDto> UpdateAsync(Guid id, SaveGoalInput input)
    {
        var goal = await GetEditableAsync(id);
        await ApplyAsync(goal, input);
        await goals.UpdateAsync(goal, autoSave: true);
        return (await ToDtosAsync(goal.ProjectId, [goal])).Single();
    }

    public async Task<GoalDto> AddUpdateAsync(Guid id, AddGoalUpdateInput input)
    {
        var goal = await GetEditableAsync(id);
        var now = clock.Now;
        goal.Status = input.Status;
        goal.Progress = input.Status == GoalStatus.Done ? 100 : input.Progress;
        goal.LastUpdatedAt = now;
        await goals.UpdateAsync(goal, autoSave: true);
        await updates.InsertAsync(
            new GoalUpdate(GuidGenerator.Create(), goal.Id, CurrentUser.GetId(), goal.Status, goal.Progress, string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(), now),
            autoSave: true);
        return (await ToDtosAsync(goal.ProjectId, [goal])).Single();
    }

    public async Task<List<GoalUpdateDto>> GetUpdatesAsync(Guid id)
    {
        var goal = await goals.GetAsync(id);
        await projectAccess.CheckAsync(goal.ProjectId, ProjectPermissions.ViewIssues);
        var list = (await updates.GetListAsync(update => update.GoalId == id)).OrderByDescending(update => update.CreatedAt).ToList();
        var people = await userDirectory.GetByIdsAsync(list.Select(update => update.AuthorId));
        return [.. list.Select(update => new GoalUpdateDto
        {
            Id = update.Id,
            Author = people.GetValueOrDefault(update.AuthorId),
            Status = update.Status,
            Progress = update.Progress,
            Note = update.Note,
            CreatedAt = update.CreatedAt,
        })];
    }

    public async Task<GoalDto> ArchiveAsync(Guid id)
    {
        var goal = await goals.GetAsync(id);
        await projectAccess.CheckAsync(goal.ProjectId, ProjectPermissions.Manage);
        goal.ArchivedAt ??= clock.Now;
        await goals.UpdateAsync(goal, autoSave: true);
        return (await ToDtosAsync(goal.ProjectId, [goal])).Single();
    }

    public async Task<GoalDto> UnarchiveAsync(Guid id)
    {
        var goal = await goals.GetAsync(id);
        await projectAccess.CheckAsync(goal.ProjectId, ProjectPermissions.Manage);
        goal.ArchivedAt = null;
        await goals.UpdateAsync(goal, autoSave: true);
        return (await ToDtosAsync(goal.ProjectId, [goal])).Single();
    }

    public async Task DeleteAsync(Guid id)
    {
        var goal = await goals.GetAsync(id);
        await projectAccess.CheckAsync(goal.ProjectId, ProjectPermissions.Manage);
        await goals.DeleteAsync(goal);
    }

    /// <summary>An archived goal is a record, so it takes no more changes until it is brought back.</summary>
    private async Task<Goal> GetEditableAsync(Guid id)
    {
        var goal = await goals.GetAsync(id);
        await projectAccess.CheckAsync(goal.ProjectId, ProjectPermissions.Manage);
        return goal.ArchivedAt is null ? goal : throw new BusinessException(GoalErrors.Archived);
    }

    private async Task ApplyAsync(Goal goal, SaveGoalInput input)
    {
        if (input.StartDate is { } start && input.TargetDate is { } target && target < start)
        {
            throw new BusinessException(GoalErrors.DatesInvalid);
        }

        if (input.OwnerId is { } ownerId && !await projectAccess.IsMemberAsync(goal.ProjectId, ownerId))
        {
            throw new BusinessException(GoalErrors.OwnerNotMember);
        }

        goal.Name = input.Name.Trim();
        goal.Description = sanitizer.Clean(input.Description).Html;
        goal.OwnerId = input.OwnerId;
        goal.StartDate = input.StartDate;
        goal.TargetDate = input.TargetDate;
    }

    private async Task<List<GoalDto>> ToDtosAsync(Guid projectId, List<Goal> list)
    {
        var canEdit = await projectAccess.IsGrantedAsync(projectId, ProjectPermissions.Manage);
        var owners = await userDirectory.GetByIdsAsync(list.Where(goal => goal.OwnerId.HasValue).Select(goal => goal.OwnerId!.Value));
        return [.. list.Select(goal => new GoalDto
        {
            Id = goal.Id,
            Name = goal.Name,
            Description = goal.Description,
            Owner = goal.OwnerId is { } ownerId ? owners.GetValueOrDefault(ownerId) : null,
            StartDate = goal.StartDate,
            TargetDate = goal.TargetDate,
            Status = goal.Status,
            Progress = goal.Progress,
            ArchivedAt = goal.ArchivedAt,
            CreatedAt = goal.CreationTime,
            LastUpdatedAt = goal.LastUpdatedAt,
            CanEdit = canEdit && goal.ArchivedAt is null,
        })];
    }
}
