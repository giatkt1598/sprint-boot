using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace SprintBoot.Tickets;

/// <summary>The rich text page that introduces a space; one per space, made on the first save.</summary>
public class ProjectIntroduction : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public string Content { get; set; } = string.Empty;

    public Guid UpdatedById { get; set; }

    public DateTime UpdatedAt { get; set; }

    protected ProjectIntroduction()
    {
    }

    public ProjectIntroduction(Guid id, Guid projectId)
        : base(id)
    {
        ProjectId = projectId;
    }
}

[Authorize]
public class ProjectIntroductionAppService(
    IRepository<ProjectIntroduction, Guid> introductions,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    RichTextSanitizer sanitizer,
    RichTextFileStore richTextFiles,
    IClock clock)
    : ApplicationService, IProjectIntroductionAppService
{
    public async Task<ProjectIntroductionDto> GetAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);
        return await ToDtoAsync(projectId, await introductions.FirstOrDefaultAsync(item => item.ProjectId == projectId));
    }

    public async Task<ProjectIntroductionDto> UpdateAsync(Guid projectId, UpdateProjectIntroductionInput input)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.Manage);
        var introduction = await introductions.FirstOrDefaultAsync(item => item.ProjectId == projectId);
        var isNew = introduction is null;
        introduction ??= new ProjectIntroduction(GuidGenerator.Create(), projectId);
        introduction.Content = sanitizer.Clean(input.Content).Html;
        introduction.UpdatedById = CurrentUser.GetId();
        introduction.UpdatedAt = clock.Now;
        if (isNew)
        {
            await introductions.InsertAsync(introduction, autoSave: true);
        }
        else
        {
            await introductions.UpdateAsync(introduction, autoSave: true);
        }

        await richTextFiles.SyncAsync(projectId, introduction.Id, introduction.Content);
        return await ToDtoAsync(projectId, introduction);
    }

    private async Task<ProjectIntroductionDto> ToDtoAsync(Guid projectId, ProjectIntroduction? introduction)
    {
        var editor = introduction is null ? null : (await userDirectory.GetByIdsAsync([introduction.UpdatedById])).GetValueOrDefault(introduction.UpdatedById);
        return new ProjectIntroductionDto
        {
            Content = richTextFiles.Sign(introduction?.Content),
            UpdatedBy = editor,
            UpdatedAt = introduction?.UpdatedAt,
            CanEdit = await projectAccess.IsGrantedAsync(projectId, ProjectPermissions.Manage),
        };
    }
}

public class TicketOverviewFacts(
    IRepository<Ticket, Guid> tickets,
    IRepository<TicketType, Guid> types,
    IRepository<TicketActivity, Guid> activities,
    IRepository<Comment, Guid> comments,
    IStatusLookup statusLookup,
    IProjectAccess projectAccess) : ITicketOverviewFacts, ITransientDependency
{
    public async Task<List<TicketOverviewRow>> GetTicketsAsync(Guid projectId)
    {
        var ticketQuery = await tickets.GetQueryableAsync();
        var rows = await ticketQuery.Where(ticket => ticket.ProjectId == projectId)
            .Select(ticket => new
            {
                ticket.Id,
                ticket.ParentId,
                ticket.TypeId,
                ticket.StatusId,
                ticket.AssigneeId,
                ticket.StoryPoints,
                ticket.CreationTime,
                UpdatedAt = ticket.LastModificationTime ?? ticket.CreationTime,
            })
            .ToListAsync();

        var ids = rows.Select(row => row.Id).ToList();
        var activityQuery = await activities.GetQueryableAsync();
        var lastMoves = await activityQuery.Where(item => item.Field == TicketFieldKeys.Status && ids.Contains(item.TicketId))
            .GroupBy(item => item.TicketId)
            .Select(group => new { group.Key, At = group.Max(item => item.CreatedAt) })
            .ToDictionaryAsync(item => item.Key, item => item.At);

        var typeMap = (await types.GetListAsync(type => type.ProjectId == projectId)).ToDictionary(type => type.Id);
        var statuses = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);

        return [.. rows.Select(row =>
        {
            var type = typeMap.GetValueOrDefault(row.TypeId);
            var status = statuses.GetValueOrDefault(row.StatusId);
            var category = status?.Category ?? StatusCategory.ToDo;
            return new TicketOverviewRow(
                row.Id,
                row.ParentId,
                type?.Name ?? string.Empty,
                type?.Icon ?? string.Empty,
                type?.Hierarchy ?? TicketHierarchy.Standard,
                category,
                status?.Name ?? string.Empty,
                row.AssigneeId,
                row.StoryPoints,
                row.CreationTime,
                row.UpdatedAt,
                category == StatusCategory.Done ? lastMoves.GetValueOrDefault(row.Id, row.UpdatedAt) : null);
        })];
    }

    public async Task<List<TicketEventRow>> GetRecentEventsAsync(Guid projectId, int take)
    {
        var ticketQuery = (await tickets.GetQueryableAsync()).Where(ticket => ticket.ProjectId == projectId);

        var created = await ticketQuery.Where(ticket => ticket.CreatorId != null)
            .OrderByDescending(ticket => ticket.CreationTime)
            .Take(take)
            .Select(ticket => new { ActorId = ticket.CreatorId!.Value, TicketId = ticket.Id, Field = (string?)null, Value = (string?)null, At = ticket.CreationTime, Kind = TicketEventKind.Created })
            .ToListAsync();

        var changed = await (from activity in await activities.GetQueryableAsync()
                             join ticket in ticketQuery on activity.TicketId equals ticket.Id
                             orderby activity.CreatedAt descending
                             select new { activity.ActorId, TicketId = ticket.Id, Field = (string?)activity.Field, Value = activity.Field == TicketFieldKeys.Status || activity.Field == TicketFieldKeys.WorkLog ? activity.NewValue : null, At = activity.CreatedAt, Kind = TicketEventKind.Changed })
            .Take(take)
            .ToListAsync();

        var commented = await (from comment in await comments.GetQueryableAsync()
                               join ticket in ticketQuery on comment.TicketId equals ticket.Id
                               orderby comment.CreationTime descending
                               select new { ActorId = comment.AuthorId, TicketId = ticket.Id, Field = (string?)null, Value = (string?)null, At = comment.CreationTime, Kind = TicketEventKind.Commented })
            .Take(take)
            .ToListAsync();

        var latest = created.Concat(changed).Concat(commented).OrderByDescending(item => item.At).Take(take).ToList();
        var ticketIds = latest.Select(item => item.TicketId).Distinct().ToList();
        var details = await ticketQuery.Where(ticket => ticketIds.Contains(ticket.Id))
            .Select(ticket => new { ticket.Id, ticket.Number, ticket.Title })
            .ToDictionaryAsync(ticket => ticket.Id);
        var key = (await projectAccess.GetRefAsync(projectId)).Key;

        return [.. latest.Where(item => details.ContainsKey(item.TicketId)).Select(item =>
        {
            var ticket = details[item.TicketId];
            return new TicketEventRow(item.ActorId, item.Kind, $"{key}-{ticket.Number}", ticket.Title, item.Field, item.Value, item.At);
        })];
    }
}
