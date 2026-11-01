using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;

namespace SprintBoot.Issues;

[ExposeServices(typeof(IIssuePlanningGateway), typeof(IssuePlanningGateway))]
public class IssuePlanningGateway(
    IRepository<Issue, Guid> issues,
    IRepository<IssueType, Guid> types,
    IRepository<Priority, Guid> priorities,
    IRepository<IssueLabel, Guid> issueLabels,
    IRepository<Label, Guid> labels,
    IRepository<IssueActivity, Guid> activities,
    IStatusLookup statusLookup,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    ICurrentActor currentActor,
    IClock clock,
    IGuidGenerator guids)
    : IIssuePlanningGateway, ITransientDependency
{
    public async Task<List<IssueCardDto>> GetCardsAsync(Guid projectId, IReadOnlyCollection<Guid> issueIds)
    {
        var ids = issueIds.ToList();
        var query = await issues.GetQueryableAsync();
        var list = await query.Where(issue => issue.ProjectId == projectId && ids.Contains(issue.Id)).ToListAsync();
        return await ToCardsAsync(projectId, Ranked(list));
    }

    public async Task<List<IssueCardDto>> GetBacklogCardsAsync(Guid projectId, IReadOnlyCollection<Guid> excludedIssueIds)
    {
        var excluded = excludedIssueIds.ToList();
        var openStatuses = (await statusLookup.GetListAsync())
            .Where(status => status.Category != StatusCategory.Done).Select(status => status.Id).ToList();
        var rowTypes = (await types.GetListAsync(type => type.ProjectId == projectId && type.Hierarchy == IssueHierarchy.Standard))
            .Select(type => type.Id).ToList();

        var query = await issues.GetQueryableAsync();
        var list = await query.Where(issue => issue.ProjectId == projectId
                && rowTypes.Contains(issue.TypeId)
                && openStatuses.Contains(issue.StatusId)
                && !excluded.Contains(issue.Id))
            .ToListAsync();
        return await ToCardsAsync(projectId, Ranked(list));
    }

    public async Task<IssueCardDto?> FindCardAsync(Guid issueId)
    {
        var issue = await issues.FindAsync(issueId);
        return issue is null ? null : (await ToCardsAsync(issue.ProjectId, [issue])).Single();
    }

    public async Task<Guid> GetProjectIdAsync(Guid issueId) => (await issues.GetAsync(issueId)).ProjectId;

    public async Task MoveRankAsync(Guid issueId, Guid? afterIssueId, Guid? beforeIssueId)
    {
        var low = afterIssueId is { } after ? await RankOfAsync(after) : null;
        var high = beforeIssueId is { } before ? await RankOfAsync(before) : null;
        if (low is not null && high is not null && string.CompareOrdinal(low, high) >= 0)
        {
            // Equal ranks can appear from concurrent creates; sort after the lower neighbour.
            high = null;
        }

        var rank = RankMath.Between(low, high);
        var query = await issues.GetQueryableAsync();
        // Bypasses the concurrency stamp on purpose: ordering must not conflict with someone editing the issue.
        await query.Where(issue => issue.Id == issueId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(issue => issue.Rank, rank));
    }

    public Task RecordActivityAsync(Guid issueId, string field, string? oldValue, string? newValue) =>
        activities.InsertAsync(new IssueActivity(guids.Create(), issueId, currentActor.Actor, field, oldValue, newValue, clock.Now));

    private async Task<string> RankOfAsync(Guid issueId)
    {
        var query = await issues.GetQueryableAsync();
        return await query.Where(issue => issue.Id == issueId).Select(issue => issue.Rank).SingleOrDefaultAsync()
            ?? throw new EntityNotFoundException(typeof(Issue), issueId);
    }

    private static List<Issue> Ranked(IEnumerable<Issue> list) =>
        [.. list.OrderBy(issue => issue.Rank, StringComparer.Ordinal).ThenBy(issue => issue.Number)];

    private async Task<List<IssueCardDto>> ToCardsAsync(Guid projectId, List<Issue> list)
    {
        var project = await projectAccess.GetRefAsync(projectId);
        var statusMap = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);
        var typeMap = (await types.GetListAsync(type => type.ProjectId == projectId)).ToDictionary(type => type.Id);
        var priorityMap = (await priorities.GetListAsync()).ToDictionary(priority => priority.Id);
        var people = list.SelectMany(issue => new[] { issue.AssigneeId, issue.ReporterId }).Where(id => id.HasValue).Select(id => id!.Value);
        var users = await userDirectory.GetByIdsAsync(people);

        var ids = list.Select(issue => issue.Id).ToList();
        var links = await issueLabels.GetListAsync(link => ids.Contains(link.IssueId));
        var labelIds = links.Select(link => link.LabelId).Distinct().ToList();
        var labelMap = (await labels.GetListAsync(label => labelIds.Contains(label.Id))).ToDictionary(label => label.Id, label => label.Name);

        var parentIds = list.Where(issue => issue.ParentId.HasValue).Select(issue => issue.ParentId!.Value).Distinct().ToList();
        var parents = (await issues.GetListAsync(issue => parentIds.Contains(issue.Id))).ToDictionary(issue => issue.Id);

        return [.. list.Select(issue => new IssueCardDto
        {
            Id = issue.Id,
            Key = $"{project.Key}-{issue.Number}",
            Title = issue.Title,
            Type = IssueMapper.ToDto(typeMap[issue.TypeId]),
            Status = statusMap[issue.StatusId],
            Priority = IssueMapper.ToDto(priorityMap[issue.PriorityId]),
            Assignee = issue.AssigneeId is { } assignee ? users.GetValueOrDefault(assignee) : null,
            Reporter = issue.ReporterId is { } reporter ? users.GetValueOrDefault(reporter) : null,
            Labels = [.. links.Where(link => link.IssueId == issue.Id).Select(link => labelMap[link.LabelId]).Order()],
            EpicId = issue.ParentId,
            EpicTitle = issue.ParentId is { } parent && parents.TryGetValue(parent, out var epic) ? epic.Title : null,
            StoryPoints = issue.StoryPoints,
            CreatedAt = issue.CreationTime,
            UpdatedAt = issue.LastModificationTime ?? issue.CreationTime,
            ConcurrencyStamp = issue.ConcurrencyStamp,
        })];
    }
}
