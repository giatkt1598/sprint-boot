using Microsoft.AspNetCore.Authorization;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace SprintBoot.Issues;

[Authorize]
public class WorkLogAppService(
    IRepository<Issue, Guid> issues,
    IRepository<WorkLog, Guid> workLogs,
    IRepository<IssueActivity, Guid> activities,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    ICurrentActor currentActor,
    IClock clock)
    : ApplicationService, IWorkLogAppService
{
    public async Task<WorkLogListDto> GetListAsync(Guid issueId)
    {
        var issue = await issues.GetAsync(issueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);

        var list = await workLogs.GetListAsync(log => log.IssueId == issueId);
        return new WorkLogListDto
        {
            TotalMinutes = list.Sum(log => log.Minutes),
            Items = await ToDtosAsync(list.OrderByDescending(log => log.WorkDate)),
        };
    }

    public async Task<WorkLogDto> CreateAsync(CreateWorkLogInput input)
    {
        var issue = await issues.GetAsync(input.IssueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.EditIssues);
        EnsureMinutes(input.Minutes);

        var log = new WorkLog(GuidGenerator.Create(), issue.Id, CurrentUser.GetId(), input.Minutes, input.WorkDate, input.Note);
        await workLogs.InsertAsync(log, autoSave: true);
        await RecordAsync(issue.Id, null, input.Minutes.ToString());
        return (await ToDtosAsync([log])).Single();
    }

    public async Task<WorkLogDto> UpdateAsync(Guid id, UpdateWorkLogInput input)
    {
        var log = await workLogs.GetAsync(id);
        var issue = await issues.GetAsync(log.IssueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);
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
        await RecordAsync(issue.Id, before.ToString(), input.Minutes.ToString());
        return (await ToDtosAsync([log])).Single();
    }

    public async Task DeleteAsync(Guid id)
    {
        var log = await workLogs.GetAsync(id);
        var issue = await issues.GetAsync(log.IssueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);
        if (log.AuthorId != CurrentUser.GetId()
            && !await projectAccess.IsGrantedAsync(issue.ProjectId, ProjectPermissions.DeleteAnyWorkLog))
        {
            throw new AbpAuthorizationException("Missing permission to delete this work log");
        }

        await workLogs.DeleteAsync(log);
        await RecordAsync(issue.Id, log.Minutes.ToString(), null);
    }

    private static void EnsureMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            throw new BusinessException(IssueErrors.WorkLogInvalidTime);
        }
    }

    private Task RecordAsync(Guid issueId, string? oldValue, string? newValue) =>
        activities.InsertAsync(new IssueActivity(
            GuidGenerator.Create(), issueId, currentActor.Actor, IssueFieldKeys.WorkLog, oldValue, newValue, clock.Now));

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
