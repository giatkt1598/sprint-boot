using Microsoft.AspNetCore.Authorization;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace SprintBoot.Tickets;

[Authorize]
public class WorkLogAppService(
    IRepository<Ticket, Guid> tickets,
    IRepository<WorkLog, Guid> workLogs,
    IRepository<TicketActivity, Guid> activities,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    ICurrentActor currentActor,
    IClock clock)
    : ApplicationService, IWorkLogAppService
{
    public async Task<WorkLogListDto> GetListAsync(Guid ticketId)
    {
        var ticket = await tickets.GetAsync(ticketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);

        var list = await workLogs.GetListAsync(log => log.TicketId == ticketId);
        return new WorkLogListDto
        {
            TotalMinutes = list.Sum(log => log.Minutes),
            Items = await ToDtosAsync(list.OrderByDescending(log => log.WorkDate)),
        };
    }

    public async Task<WorkLogDto> CreateAsync(CreateWorkLogInput input)
    {
        var ticket = await tickets.GetAsync(input.TicketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.EditTickets);
        EnsureMinutes(input.Minutes);

        var log = new WorkLog(GuidGenerator.Create(), ticket.Id, CurrentUser.GetId(), input.Minutes, input.WorkDate, input.Note);
        await workLogs.InsertAsync(log, autoSave: true);
        await RecordAsync(ticket.Id, null, input.Minutes.ToString());
        return (await ToDtosAsync([log])).Single();
    }

    public async Task<WorkLogDto> UpdateAsync(Guid id, UpdateWorkLogInput input)
    {
        var log = await workLogs.GetAsync(id);
        var ticket = await tickets.GetAsync(log.TicketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);
        await projectAccess.CheckWritableAsync(ticket.ProjectId);
        if (log.AuthorId != CurrentUser.GetId())
        {
            throw new AbpAuthorizationException("Only the author can edit a work log");
        }

        EnsureMinutes(input.Minutes);
        var before = log.Minutes;
        log.Minutes = input.Minutes;
        log.WorkDate = input.WorkDate;
        log.Note = input.Note;
        await workLogs.UpdateAsync(log, autoSave: true);
        await RecordAsync(ticket.Id, before.ToString(), input.Minutes.ToString());
        return (await ToDtosAsync([log])).Single();
    }

    public async Task DeleteAsync(Guid id)
    {
        var log = await workLogs.GetAsync(id);
        var ticket = await tickets.GetAsync(log.TicketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);
        await projectAccess.CheckWritableAsync(ticket.ProjectId);
        if (log.AuthorId != CurrentUser.GetId()
            && !await projectAccess.IsGrantedAsync(ticket.ProjectId, ProjectPermissions.DeleteAnyWorkLog))
        {
            throw new AbpAuthorizationException("Missing permission to delete this work log");
        }

        await workLogs.DeleteAsync(log);
        await RecordAsync(ticket.Id, log.Minutes.ToString(), null);
    }

    private static void EnsureMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            throw new BusinessException(TicketErrors.WorkLogInvalidTime);
        }
    }

    private Task RecordAsync(Guid ticketId, string? oldValue, string? newValue) =>
        activities.InsertAsync(new TicketActivity(
            GuidGenerator.Create(), ticketId, currentActor.Actor, TicketFieldKeys.WorkLog, oldValue, newValue, clock.Now));

    private async Task<List<WorkLogDto>> ToDtosAsync(IEnumerable<WorkLog> list)
    {
        var items = list.ToList();
        var users = await userDirectory.GetByIdsAsync(items.Select(log => log.AuthorId));
        return [.. items.Select(log => new WorkLogDto
        {
            Id = log.Id,
            Author = users.GetValueOrDefault(log.AuthorId),
            Minutes = log.Minutes,
            WorkDate = log.WorkDate,
            Note = log.Note,
        })];
    }
}
