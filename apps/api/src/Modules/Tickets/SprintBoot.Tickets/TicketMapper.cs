using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Tickets;

public class TicketMapper(
    IRepository<Ticket, Guid> tickets,
    IRepository<TicketType, Guid> types,
    IRepository<Priority, Guid> priorities,
    IRepository<TicketLabel, Guid> ticketLabels,
    IRepository<Label, Guid> labels,
    IRepository<WorkLog, Guid> workLogs,
    IStatusLookup statusLookup,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    ITicketSprintNames sprintNames,
    RichTextFileStore richTextFiles,
    ITicketFieldPolicy fieldPolicy) : ITransientDependency
{
    public static TicketTypeDto ToDto(TicketType type) => new()
    {
        Id = type.Id,
        Name = type.Name,
        Icon = type.Icon,
        Hierarchy = type.Hierarchy,
    };

    public static PriorityDto ToDto(Priority priority) => new()
    {
        Id = priority.Id,
        Name = priority.Name,
        Order = priority.Order,
    };

    public async Task<List<TicketSummaryDto>> ToSummariesAsync(IReadOnlyCollection<Ticket> list)
    {
        var projects = await projectAccess.GetRefsAsync(list.Select(ticket => ticket.ProjectId));
        var typeIds = list.Select(ticket => ticket.TypeId).Distinct().ToList();
        var typeMap = (await types.GetListAsync(type => typeIds.Contains(type.Id))).ToDictionary(type => type.Id);
        var priorityMap = (await priorities.GetListAsync()).ToDictionary(priority => priority.Id);
        var statusMap = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);
        var users = await userDirectory.GetByIdsAsync(
            list.SelectMany(ticket => new[] { ticket.AssigneeId, ticket.ReporterId }).Where(id => id.HasValue).Select(id => id!.Value).Distinct());
        var sprints = await sprintNames.GetAsync([.. list.Select(ticket => ticket.Id)]);

        return [.. list.Select(ticket => new TicketSummaryDto
        {
            Id = ticket.Id,
            Key = $"{projects[ticket.ProjectId].Key}-{ticket.Number}",
            Title = ticket.Title,
            ProjectKey = projects[ticket.ProjectId].Key,
            Type = ToDto(typeMap[ticket.TypeId]),
            Status = statusMap[ticket.StatusId],
            Priority = ToDto(priorityMap[ticket.PriorityId]),
            Assignee = ticket.AssigneeId is { } id ? users.GetValueOrDefault(id) : null,
            Reporter = ticket.ReporterId is { } reporterId ? users.GetValueOrDefault(reporterId) : null,
            SprintName = sprints.GetValueOrDefault(ticket.Id),
            ParentId = ticket.ParentId,
            StartDate = ticket.StartDate,
            DueDate = ticket.DueDate,
            CreatedAt = ticket.CreationTime,
            UpdatedAt = ticket.LastModificationTime ?? ticket.CreationTime,
            ConcurrencyStamp = ticket.ConcurrencyStamp,
        })];
    }

    public async Task<TicketDto> ToDtoAsync(Ticket ticket)
    {
        var project = await projectAccess.GetRefAsync(ticket.ProjectId);
        var statusMap = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);
        var type = await types.GetAsync(ticket.TypeId);
        var priority = await priorities.GetAsync(ticket.PriorityId);

        var children = await tickets.GetListAsync(child => child.ParentId == ticket.Id);
        var userIds = children.Select(child => child.AssigneeId).Append(ticket.AssigneeId).Append(ticket.ReporterId)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct();
        var users = await userDirectory.GetByIdsAsync(userIds);

        var parent = ticket.ParentId is { } parentId ? await tickets.FindAsync(parentId) : null;
        var related = children.Concat(parent is null ? [] : [parent]).ToList();
        var relatedTypeIds = related.Select(item => item.TypeId).Distinct().ToList();
        var relatedTypes = (await types.GetListAsync(item => relatedTypeIds.Contains(item.Id))).ToDictionary(item => item.Id);

        TicketRefDto ToRef(Ticket item) => new()
        {
            Id = item.Id,
            Key = $"{project.Key}-{item.Number}",
            Title = item.Title,
            TypeIcon = relatedTypes[item.TypeId].Icon,
            Status = statusMap[item.StatusId],
            Assignee = item.AssigneeId is { } assignee ? users.GetValueOrDefault(assignee) : null,
            ConcurrencyStamp = item.ConcurrencyStamp,
        };

        var labelIds = (await ticketLabels.GetListAsync(link => link.TicketId == ticket.Id)).Select(link => link.LabelId).ToList();
        var labelNames = (await labels.GetListAsync(label => labelIds.Contains(label.Id))).Select(label => label.Name).Order().ToArray();
        var minutes = (await workLogs.GetListAsync(log => log.TicketId == ticket.Id)).Sum(log => log.Minutes);

        var dto = new TicketDto
        {
            Id = ticket.Id,
            Key = $"{project.Key}-{ticket.Number}",
            ProjectId = project.Id,
            ProjectKey = project.Key,
            ProjectName = project.Name,
            Type = ToDto(type),
            Title = ticket.Title,
            Description = richTextFiles.Sign(ticket.Description),
            Status = statusMap[ticket.StatusId],
            Priority = ToDto(priority),
            Assignee = ticket.AssigneeId is { } assigneeId ? users.GetValueOrDefault(assigneeId) : null,
            Reporter = ticket.ReporterId is { } reporterId ? users.GetValueOrDefault(reporterId) : null,
            Parent = parent is null ? null : ToRef(parent),
            Children = [.. children.OrderBy(child => child.Number).Select(ToRef)],
            Labels = labelNames,
            StoryPoints = ticket.StoryPoints,
            StartDate = ticket.StartDate,
            DueDate = ticket.DueDate,
            CreatedAt = ticket.CreationTime,
            UpdatedAt = ticket.LastModificationTime ?? ticket.CreationTime,
            TotalMinutesLogged = minutes,
            ConcurrencyStamp = ticket.ConcurrencyStamp,
        };

        await fieldPolicy.ApplyReadAsync(dto);
        return dto;
    }
}
