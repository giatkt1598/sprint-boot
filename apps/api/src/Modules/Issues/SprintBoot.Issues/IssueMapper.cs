using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Issues;

public class IssueMapper(
    IRepository<Issue, Guid> issues,
    IRepository<IssueType, Guid> types,
    IRepository<Priority, Guid> priorities,
    IRepository<IssueLabel, Guid> issueLabels,
    IRepository<Label, Guid> labels,
    IRepository<WorkLog, Guid> workLogs,
    IStatusLookup statusLookup,
    IProjectAccess projectAccess,
    IUserDirectory userDirectory,
    IIssueSprintNames sprintNames,
    RichTextFileStore richTextFiles,
    IIssueFieldPolicy fieldPolicy) : ITransientDependency
{
    public static IssueTypeDto ToDto(IssueType type) => new()
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

    public async Task<List<IssueSummaryDto>> ToSummariesAsync(IReadOnlyCollection<Issue> list)
    {
        var projects = await projectAccess.GetRefsAsync(list.Select(issue => issue.ProjectId));
        var typeIds = list.Select(issue => issue.TypeId).Distinct().ToList();
        var typeMap = (await types.GetListAsync(type => typeIds.Contains(type.Id))).ToDictionary(type => type.Id);
        var priorityMap = (await priorities.GetListAsync()).ToDictionary(priority => priority.Id);
        var statusMap = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);
        var users = await userDirectory.GetByIdsAsync(
            list.SelectMany(issue => new[] { issue.AssigneeId, issue.ReporterId }).Where(id => id.HasValue).Select(id => id!.Value).Distinct());
        var sprints = await sprintNames.GetAsync([.. list.Select(issue => issue.Id)]);

        return [.. list.Select(issue => new IssueSummaryDto
        {
            Id = issue.Id,
            Key = $"{projects[issue.ProjectId].Key}-{issue.Number}",
            Title = issue.Title,
            ProjectKey = projects[issue.ProjectId].Key,
            Type = ToDto(typeMap[issue.TypeId]),
            Status = statusMap[issue.StatusId],
            Priority = ToDto(priorityMap[issue.PriorityId]),
            Assignee = issue.AssigneeId is { } id ? users.GetValueOrDefault(id) : null,
            Reporter = issue.ReporterId is { } reporterId ? users.GetValueOrDefault(reporterId) : null,
            SprintName = sprints.GetValueOrDefault(issue.Id),
            CreatedAt = issue.CreationTime,
            UpdatedAt = issue.LastModificationTime ?? issue.CreationTime,
            ConcurrencyStamp = issue.ConcurrencyStamp,
        })];
    }

    public async Task<IssueDto> ToDtoAsync(Issue issue)
    {
        var project = await projectAccess.GetRefAsync(issue.ProjectId);
        var statusMap = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);
        var type = await types.GetAsync(issue.TypeId);
        var priority = await priorities.GetAsync(issue.PriorityId);

        var children = await issues.GetListAsync(child => child.ParentId == issue.Id);
        var userIds = children.Select(child => child.AssigneeId).Append(issue.AssigneeId).Append(issue.ReporterId)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct();
        var users = await userDirectory.GetByIdsAsync(userIds);

        var parent = issue.ParentId is { } parentId ? await issues.FindAsync(parentId) : null;
        var related = children.Concat(parent is null ? [] : [parent]).ToList();
        var relatedTypeIds = related.Select(item => item.TypeId).Distinct().ToList();
        var relatedTypes = (await types.GetListAsync(item => relatedTypeIds.Contains(item.Id))).ToDictionary(item => item.Id);

        IssueRefDto ToRef(Issue item) => new()
        {
            Id = item.Id,
            Key = $"{project.Key}-{item.Number}",
            Title = item.Title,
            TypeIcon = relatedTypes[item.TypeId].Icon,
            Status = statusMap[item.StatusId],
            Assignee = item.AssigneeId is { } assignee ? users.GetValueOrDefault(assignee) : null,
            ConcurrencyStamp = item.ConcurrencyStamp,
        };

        var labelIds = (await issueLabels.GetListAsync(link => link.IssueId == issue.Id)).Select(link => link.LabelId).ToList();
        var labelNames = (await labels.GetListAsync(label => labelIds.Contains(label.Id))).Select(label => label.Name).Order().ToArray();
        var minutes = (await workLogs.GetListAsync(log => log.IssueId == issue.Id)).Sum(log => log.Minutes);

        var dto = new IssueDto
        {
            Id = issue.Id,
            Key = $"{project.Key}-{issue.Number}",
            ProjectId = project.Id,
            ProjectKey = project.Key,
            ProjectName = project.Name,
            Type = ToDto(type),
            Title = issue.Title,
            Description = richTextFiles.Sign(issue.Description),
            Status = statusMap[issue.StatusId],
            Priority = ToDto(priority),
            Assignee = issue.AssigneeId is { } assigneeId ? users.GetValueOrDefault(assigneeId) : null,
            Reporter = issue.ReporterId is { } reporterId ? users.GetValueOrDefault(reporterId) : null,
            Parent = parent is null ? null : ToRef(parent),
            Children = [.. children.OrderBy(child => child.Number).Select(ToRef)],
            Labels = labelNames,
            StoryPoints = issue.StoryPoints,
            StartDate = issue.StartDate,
            DueDate = issue.DueDate,
            CreatedAt = issue.CreationTime,
            UpdatedAt = issue.LastModificationTime ?? issue.CreationTime,
            TotalMinutesLogged = minutes,
            ConcurrencyStamp = issue.ConcurrencyStamp,
        };

        await fieldPolicy.ApplyReadAsync(dto);
        return dto;
    }
}
