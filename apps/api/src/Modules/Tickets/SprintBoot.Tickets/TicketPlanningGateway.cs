using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace SprintBoot.Tickets;

[ExposeServices(typeof(ITicketPlanningGateway), typeof(TicketPlanningGateway))]
public class TicketPlanningGateway(
    IRepository<Ticket, Guid> tickets,
    IRepository<TicketType, Guid> types,
    IRepository<Priority, Guid> priorities,
    IRepository<TicketLabel, Guid> ticketLabels,
    IRepository<Label, Guid> labels,
    IRepository<TicketActivity, Guid> activities,
    IStatusLookup statusLookup,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    ICurrentActor currentActor,
    IClock clock,
    IGuidGenerator guids,
    CustomFieldStore customFieldStore)
    : ITicketPlanningGateway, ITransientDependency
{
    public async Task<List<TicketCardDto>> GetCardsAsync(Guid projectId, IReadOnlyCollection<Guid> ticketIds)
    {
        var ids = ticketIds.ToList();
        var query = await tickets.GetQueryableAsync();
        var list = await query.Where(ticket => ticket.ProjectId == projectId && ids.Contains(ticket.Id)).ToListAsync();
        return await ToCardsAsync(projectId, Ranked(list));
    }

    public async Task<List<TicketCardDto>> GetBacklogCardsAsync(Guid projectId, IReadOnlyCollection<Guid> excludedTicketIds)
    {
        var excluded = excludedTicketIds.ToList();
        var openStatuses = (await statusLookup.GetListAsync())
            .Where(status => status.Category != StatusCategory.Done).Select(status => status.Id).ToList();
        var rowTypes = (await types.GetListAsync(type => type.ProjectId == projectId && type.Hierarchy == TicketHierarchy.Standard))
            .Select(type => type.Id).ToList();

        var query = await tickets.GetQueryableAsync();
        var list = await query.Where(ticket => ticket.ProjectId == projectId
                && rowTypes.Contains(ticket.TypeId)
                && openStatuses.Contains(ticket.StatusId)
                && !excluded.Contains(ticket.Id))
            .ToListAsync();
        return await ToCardsAsync(projectId, Ranked(list));
    }

    public async Task<TicketCardDto?> FindCardAsync(Guid ticketId)
    {
        var ticket = await tickets.FindAsync(ticketId);
        return ticket is null ? null : (await ToCardsAsync(ticket.ProjectId, [ticket])).Single();
    }

    public async Task<Guid> GetProjectIdAsync(Guid ticketId) => (await tickets.GetAsync(ticketId)).ProjectId;

    public async Task MoveRankAsync(Guid ticketId, Guid? afterTicketId, Guid? beforeTicketId)
    {
        var low = afterTicketId is { } after ? await RankOfAsync(after) : null;
        var high = beforeTicketId is { } before ? await RankOfAsync(before) : null;
        if (low is not null && high is not null && string.CompareOrdinal(low, high) >= 0)
        {
            // Equal ranks can appear from concurrent creates; sort after the lower neighbour.
            high = null;
        }

        var rank = RankMath.Between(low, high);
        var query = await tickets.GetQueryableAsync();
        // Bypasses the concurrency stamp on purpose: ordering must not conflict with someone editing the ticket.
        await query.Where(ticket => ticket.Id == ticketId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(ticket => ticket.Rank, rank));
    }

    public Task RecordActivityAsync(Guid ticketId, string field, string? oldValue, string? newValue) =>
        activities.InsertAsync(new TicketActivity(guids.Create(), ticketId, currentActor.Actor, field, oldValue, newValue, clock.Now));

    private async Task<string> RankOfAsync(Guid ticketId)
    {
        var query = await tickets.GetQueryableAsync();
        return await query.Where(ticket => ticket.Id == ticketId).Select(ticket => ticket.Rank).SingleOrDefaultAsync()
            ?? throw new EntityNotFoundException(typeof(Ticket), ticketId);
    }

    private static List<Ticket> Ranked(IEnumerable<Ticket> list) =>
        [.. list.OrderBy(ticket => ticket.Rank, StringComparer.Ordinal).ThenBy(ticket => ticket.Number)];

    private async Task<List<TicketCardDto>> ToCardsAsync(Guid projectId, List<Ticket> list)
    {
        var project = await projectAccess.GetRefAsync(projectId);
        var statusMap = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);
        var typeMap = (await types.GetListAsync(type => type.ProjectId == projectId)).ToDictionary(type => type.Id);
        var priorityMap = (await priorities.GetListAsync()).ToDictionary(priority => priority.Id);
        var people = list.SelectMany(ticket => new[] { ticket.AssigneeId, ticket.ReporterId }).Where(id => id.HasValue).Select(id => id!.Value);
        var users = await userDirectory.GetByIdsAsync(people);

        var ids = list.Select(ticket => ticket.Id).ToList();
        var links = await ticketLabels.GetListAsync(link => ids.Contains(link.TicketId));
        var labelIds = links.Select(link => link.LabelId).Distinct().ToList();
        var labelMap = (await labels.GetListAsync(label => labelIds.Contains(label.Id))).ToDictionary(label => label.Id, label => label.Name);

        var parentIds = list.Where(ticket => ticket.ParentId.HasValue).Select(ticket => ticket.ParentId!.Value).Distinct().ToList();
        var parents = (await tickets.GetListAsync(ticket => parentIds.Contains(ticket.Id))).ToDictionary(ticket => ticket.Id);
        var custom = await customFieldStore.ReadAsync(list);

        return [.. list.Select(ticket => new TicketCardDto
        {
            Id = ticket.Id,
            Key = $"{project.Key}-{ticket.Number}",
            Title = ticket.Title,
            Type = TicketMapper.ToDto(typeMap[ticket.TypeId]),
            Status = statusMap[ticket.StatusId],
            Priority = TicketMapper.ToDto(priorityMap[ticket.PriorityId]),
            Assignee = ticket.AssigneeId is { } assignee ? users.GetValueOrDefault(assignee) : null,
            Reporter = ticket.ReporterId is { } reporter ? users.GetValueOrDefault(reporter) : null,
            Labels = [.. links.Where(link => link.TicketId == ticket.Id).Select(link => labelMap[link.LabelId]).Order()],
            EpicId = ticket.ParentId,
            EpicTitle = ticket.ParentId is { } parent && parents.TryGetValue(parent, out var epic) ? epic.Title : null,
            StoryPoints = ticket.StoryPoints,
            CreatedAt = ticket.CreationTime,
            UpdatedAt = ticket.LastModificationTime ?? ticket.CreationTime,
            ConcurrencyStamp = ticket.ConcurrencyStamp,
            // Cards only show what has a value.
            CustomFields = [.. custom[ticket.Id].Where(CustomFieldStore.HasValue)],
        })];
    }
}
