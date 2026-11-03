using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace SprintBoot.Issues;

[Authorize]
[Volo.Abp.DependencyInjection.ExposeServices(typeof(IIssueAppService), typeof(IssueAppService))]
public partial class IssueAppService(
    IRepository<Issue, Guid> issues,
    IRepository<IssueType, Guid> types,
    IRepository<Priority, Guid> priorities,
    IRepository<Label, Guid> labels,
    IRepository<IssueLabel, Guid> issueLabels,
    IRepository<IssueActivity, Guid> activities,
    IProjectAccess projectAccess,
    IIssueFieldPolicy fieldPolicy,
    IWorkflowRuntime workflowRuntime,
    IStatusLookup statusLookup,
    IUserDirectory userDirectory,
    ICurrentActor currentActor,
    IClock clock,
    IDataFilter dataFilter,
    IDistributedEventBus eventBus,
    IssueMapper mapper,
    WatcherStore watcherStore,
    RichTextSanitizer sanitizer,
    RichTextFileStore richTextFiles)
    : ApplicationService, IIssueAppService
{
    [GeneratedRegex("^([A-Z][A-Z0-9]{1,9})-([0-9]+)$")]
    private static partial Regex IssueKeyPattern();

    public async Task<List<IssueTypeDto>> GetTypesAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        var list = await types.GetListAsync(type => type.ProjectId == projectId);
        return [.. list.OrderByDescending(type => type.Hierarchy).ThenBy(type => type.Name).Select(IssueMapper.ToDto)];
    }

    public async Task<List<PriorityDto>> GetPrioritiesAsync()
    {
        var list = await priorities.GetListAsync();
        return [.. list.OrderBy(priority => priority.Order).Select(IssueMapper.ToDto)];
    }

    public async Task<IssueDto> GetByKeyAsync(string key)
    {
        var issue = await FindByKeyAsync(key) ?? throw new EntityNotFoundException(typeof(Issue), key);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);
        return await mapper.ToDtoAsync(issue);
    }

    public async Task<IssueDto> CreateAsync(CreateIssueInput input)
    {
        await projectAccess.CheckAsync(input.ProjectId, ProjectPermissions.EditIssues);

        var type = await types.FirstOrDefaultAsync(item => item.Id == input.TypeId && item.ProjectId == input.ProjectId)
            ?? throw new BusinessException(IssueErrors.ProjectMismatch);
        await EnsureParentIsValidAsync(input.ProjectId, type, input.ParentId);
        EnsureDates(input.StartDate, input.DueDate);

        var priorityId = input.PriorityId ?? await GetDefaultPriorityIdAsync();
        var initial = await workflowRuntime.GetInitialStateAsync(input.ProjectId, type.Id);
        var number = await projectAccess.NextIssueNumberAsync(input.ProjectId);

        var actor = currentActor.Actor;
        var issue = new Issue(GuidGenerator.Create(), input.ProjectId, number, type.Id, input.Title.Trim(), initial.StatusId, initial.VersionId, priorityId)
        {
            AssigneeId = input.AssigneeId,
            AssigneeType = input.AssigneeId.HasValue ? ActorType.User : null,
            ReporterId = input.ReporterId ?? actor.Id,
            ParentId = input.ParentId,
            StoryPoints = input.StoryPoints,
            StartDate = input.StartDate,
            DueDate = input.DueDate,
        };
        var (html, text) = sanitizer.Clean(input.Description);
        issue.SetDescription(html, text);
        issue.SetRank(RankMath.Between(await GetLastRankAsync(input.ProjectId), null));

        await issues.InsertAsync(issue, autoSave: true);
        await SetLabelsAsync(issue, input.Labels ?? []);
        await richTextFiles.SyncAsync(issue.ProjectId, issue.Id, issue.Description);
        await watcherStore.EnsureAsync(issue.Id, issue.ReporterId, issue.AssigneeId);
        await eventBus.PublishAsync(
            new IssueCreatedEto
            {
                EventId = GuidGenerator.Create(),
                IssueId = issue.Id,
                ProjectId = issue.ProjectId,
                ActorId = actor.Id,
                Mentioned = RichTextSanitizer.Mentions(issue.Description),
            },
            useOutbox: true);

        return await mapper.ToDtoAsync(issue);
    }

    public async Task<IssueDto> UpdateAsync(Guid id, UpdateIssueInput input)
    {
        var issue = await issues.GetAsync(id);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.EditIssues);

        if (issue.ConcurrencyStamp != input.ConcurrencyStamp)
        {
            throw new AbpDbConcurrencyException("The issue was changed by someone else.");
        }

        var changed = new List<string>();
        foreach (var (field, value) in input.Fields)
        {
            if (field == IssueFieldKeys.Status)
            {
                throw new BusinessException(IssueErrors.FieldNotWritable).WithData("field", field);
            }

            if (!await fieldPolicy.CanWriteAsync(issue.ProjectId, field))
            {
                throw new BusinessException(IssueErrors.FieldNotWritable).WithData("field", field);
            }

            var change = await ApplyFieldAsync(issue, field, value);
            if (change is not null)
            {
                changed.Add(field);
                await activities.InsertAsync(new IssueActivity(
                    GuidGenerator.Create(), issue.Id, currentActor.Actor, field, change.Value.Old, change.Value.New, clock.Now));
            }
        }

        EnsureDates(issue.StartDate, issue.DueDate);

        if (changed.Count > 0)
        {
            await issues.UpdateAsync(issue, autoSave: true);
            await watcherStore.EnsureAsync(issue.Id, issue.AssigneeId);
            await eventBus.PublishAsync(
                new IssueUpdatedEto
                {
                    EventId = GuidGenerator.Create(),
                    IssueId = issue.Id,
                    ProjectId = issue.ProjectId,
                    ActorId = currentActor.Actor.Id,
                    Fields = [.. changed],
                    Mentioned = changed.Contains(IssueFieldKeys.Description) ? RichTextSanitizer.Mentions(issue.Description) : [],
                },
                useOutbox: true);
        }

        return await mapper.ToDtoAsync(issue);
    }

    public async Task DeleteAsync(Guid id)
    {
        var issue = await issues.GetAsync(id);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);

        var canDeleteAny = await projectAccess.IsGrantedAsync(issue.ProjectId, ProjectPermissions.DeleteAnyIssue);
        var canDeleteOwn = issue.CreatorId == CurrentUser.Id
            && await projectAccess.IsGrantedAsync(issue.ProjectId, ProjectPermissions.DeleteOwnIssue);
        if (!canDeleteAny && !canDeleteOwn)
        {
            throw new Volo.Abp.Authorization.AbpAuthorizationException("Missing permission to delete this issue");
        }

        var subtasks = await issues.GetListAsync(child => child.ParentId == id);
        await issues.DeleteAsync(issue, autoSave: true);
        await issues.DeleteManyAsync(subtasks);
    }

    public async Task<IssueSearchResultDto> GetSearchAsync(IssueSearchInput input)
    {
        if (input.ProjectId is { } projectId)
        {
            await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        }

        var visible = await projectAccess.GetVisibleProjectIdsAsync();
        var query = await issues.GetQueryableAsync();

        if (visible is not null)
        {
            query = query.Where(issue => visible.Contains(issue.ProjectId));
        }

        var text = input.Text?.Trim();
        string? exactKey = null;
        if (!string.IsNullOrEmpty(text))
        {
            var exact = await FindByKeyAsync(text.ToUpperInvariant());
            if (exact is not null && (visible is null || visible.Contains(exact.ProjectId))
                && (input.ProjectId is null || exact.ProjectId == input.ProjectId))
            {
                exactKey = text.ToUpperInvariant();
                query = query.Where(issue => issue.Id == exact.Id);
            }
            else
            {
                var pattern = $"%{text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
                query = query.Where(issue =>
                    issue.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("simple", text))
                    || EF.Functions.ILike(issue.Title, pattern));
            }
        }

        query = ApplyFilters(query, input);
        if (input.Labels is { Length: > 0 } names)
        {
            var labelQuery = await labels.GetQueryableAsync();
            var linkQuery = await issueLabels.GetQueryableAsync();
            var labelledIds = from link in linkQuery
                              join label in labelQuery on link.LabelId equals label.Id
                              where names.Contains(label.Name)
                              select link.IssueId;
            query = query.Where(issue => labelledIds.Contains(issue.Id));
        }

        var total = await query.LongCountAsync();
        var page = await query
            .OrderByDescending(issue => issue.LastModificationTime ?? issue.CreationTime)
            .ThenBy(issue => issue.Id)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToListAsync();

        return new IssueSearchResultDto
        {
            TotalCount = total,
            Items = await mapper.ToSummariesAsync(page),
            ExactKey = exactKey,
        };
    }

    public async Task<List<string>> GetLabelsAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        var list = await labels.GetListAsync(label => label.ProjectId == projectId);
        return [.. list.Select(label => label.Name).Order()];
    }

    public async Task<List<EpicRowDto>> GetEpicsAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);

        var epicTypes = await types.GetListAsync(type => type.ProjectId == projectId && type.Hierarchy == IssueHierarchy.Epic);
        var epicTypeIds = epicTypes.Select(type => type.Id).ToList();
        var epics = await issues.GetListAsync(issue => issue.ProjectId == projectId && epicTypeIds.Contains(issue.TypeId));
        var epicIds = epics.Select(epic => epic.Id).ToList();
        var children = await issues.GetListAsync(issue => issue.ParentId != null && epicIds.Contains(issue.ParentId.Value));

        var project = await projectAccess.GetRefAsync(projectId);
        var statuses = (await statusLookup.GetListAsync()).ToDictionary(status => status.Id);
        var leads = await userDirectory.GetByIdsAsync(epics.Where(epic => epic.AssigneeId.HasValue).Select(epic => epic.AssigneeId!.Value));
        var priorityMap = (await priorities.GetListAsync()).ToDictionary(priority => priority.Id);

        return [.. epics.OrderBy(epic => epic.Number).Select(epic =>
        {
            var own = children.Where(child => child.ParentId == epic.Id).ToList();
            return new EpicRowDto
            {
                Id = epic.Id,
                Key = $"{project.Key}-{epic.Number}",
                Title = epic.Title,
                Status = statuses[epic.StatusId],
                Priority = IssueMapper.ToDto(priorityMap[epic.PriorityId]),
                DueDate = epic.DueDate,
                Lead = epic.AssigneeId is { } lead ? leads.GetValueOrDefault(lead) : null,
                ConcurrencyStamp = epic.ConcurrencyStamp,
                Total = own.Count,
                Done = own.Count(child => statuses[child.StatusId].Category == StatusCategory.Done),
                InProgress = own.Count(child => statuses[child.StatusId].Category == StatusCategory.InProgress),
            };
        })];
    }

    public async Task<List<IssueActivityDto>> GetActivityAsync(Guid id)
    {
        var issue = await issues.GetAsync(id);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);

        var list = await activities.GetListAsync(activity => activity.IssueId == id);
        var names = await ActivityNamesAsync(list);
        var users = await userDirectory.GetByIdsAsync(list.Select(activity => activity.ActorId));
        string? Show(string? value) => value is not null && Guid.TryParse(value, out var key) && names.TryGetValue(key, out var name) ? name : value;
        return [.. list.OrderByDescending(activity => activity.CreatedAt).Select(activity => new IssueActivityDto
        {
            Id = activity.Id,
            Actor = users.GetValueOrDefault(activity.ActorId),
            ActorType = activity.ActorType,
            Field = activity.Field,
            OldValue = Show(activity.OldValue),
            NewValue = Show(activity.NewValue),
            CreatedAt = activity.CreatedAt,
        })];
    }

    /// <summary>
    /// History stores ids for fields that point at people, priorities or issues, so a rename shows
    /// everywhere; readers get the current name instead.
    /// </summary>
    private async Task<Dictionary<Guid, string>> ActivityNamesAsync(List<IssueActivity> list)
    {
        HashSet<Guid> IdsOf(params string[] fields) =>
            [.. list.Where(activity => fields.Contains(activity.Field))
                .SelectMany(activity => new[] { activity.OldValue, activity.NewValue })
                .Select(value => Guid.TryParse(value, out var key) ? key : Guid.Empty)
                .Where(key => key != Guid.Empty)];

        var names = new Dictionary<Guid, string>();
        var people = IdsOf(IssueFieldKeys.Assignee, IssueFieldKeys.Reporter);
        foreach (var (key, user) in await userDirectory.GetByIdsAsync(people))
        {
            names[key] = user.Name;
        }

        var priorityIds = IdsOf(IssueFieldKeys.Priority);
        foreach (var priority in await priorities.GetListAsync(priority => priorityIds.Contains(priority.Id)))
        {
            names[priority.Id] = priority.Name;
        }

        // A parent may have been deleted since; its key still says which one it was.
        var parentIds = IdsOf(IssueFieldKeys.Parent);
        using (dataFilter.Disable<ISoftDelete>())
        {
            var parents = await issues.GetListAsync(issue => parentIds.Contains(issue.Id));
            var projects = await projectAccess.GetRefsAsync(parents.Select(parent => parent.ProjectId));
            foreach (var parent in parents)
            {
                names[parent.Id] = projects.TryGetValue(parent.ProjectId, out var project) ? $"{project.Key}-{parent.Number} {parent.Title}" : parent.Title;
            }
        }

        return names;
    }

    [Authorize(SprintBootPermissions.RestoreIssues)]
    public async Task<PagedResultDto<DeletedIssueDto>> GetDeletedListAsync(PagedResultRequestDto input)
    {
        using (dataFilter.Disable<ISoftDelete>())
        {
            var query = (await issues.GetQueryableAsync()).Where(issue => issue.IsDeleted);
            var subtaskIds = query.Where(issue => issue.ParentId != null).Select(issue => issue.Id);
            var roots = query.Where(issue => !subtaskIds.Contains(issue.Id) || !query.Any(parent => parent.Id == issue.ParentId));

            var total = await roots.LongCountAsync();
            var page = await roots.OrderByDescending(issue => issue.DeletionTime)
                .Skip(input.SkipCount).Take(input.MaxResultCount).ToListAsync();
            var ids = page.Select(issue => issue.Id).ToList();
            var childCounts = await query.Where(issue => issue.ParentId != null && ids.Contains(issue.ParentId.Value))
                .GroupBy(issue => issue.ParentId!.Value)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.Key, item => item.Count);

            var projects = await projectAccess.GetRefsAsync(page.Select(issue => issue.ProjectId));
            var users = await userDirectory.GetByIdsAsync(page.Where(i => i.DeleterId.HasValue).Select(i => i.DeleterId!.Value));
            return new PagedResultDto<DeletedIssueDto>(total, [.. page.Select(issue => new DeletedIssueDto
            {
                Id = issue.Id,
                Key = $"{projects[issue.ProjectId].Key}-{issue.Number}",
                Title = issue.Title,
                ProjectName = projects[issue.ProjectId].Name,
                SubtaskCount = childCounts.GetValueOrDefault(issue.Id),
                DeletedBy = issue.DeleterId is { } deleter ? users.GetValueOrDefault(deleter) : null,
                DeletedAt = issue.DeletionTime!.Value,
            })]);
        }
    }

    [Authorize(SprintBootPermissions.RestoreIssues)]
    public async Task RestoreAsync(Guid id)
    {
        using (dataFilter.Disable<ISoftDelete>())
        {
            var issue = await issues.GetAsync(id);
            var deletedAt = issue.DeletionTime;
            // Subtasks removed earlier on their own stay deleted.
            var subtasks = await issues.GetListAsync(child => child.ParentId == id && child.IsDeleted && child.DeletionTime >= deletedAt);
            foreach (var item in subtasks.Append(issue))
            {
                item.IsDeleted = false;
                item.DeletionTime = null;
                item.DeleterId = null;
                await issues.UpdateAsync(item);
            }
        }
    }

    private static IQueryable<Issue> ApplyFilters(IQueryable<Issue> query, IssueSearchInput input)
    {
        if (input.ProjectId is { } projectId)
        {
            query = query.Where(issue => issue.ProjectId == projectId);
        }

        if (input.TypeIds is { Length: > 0 } typeIds)
        {
            query = query.Where(issue => typeIds.Contains(issue.TypeId));
        }

        if (input.StatusIds is { Length: > 0 } statusIds)
        {
            query = query.Where(issue => statusIds.Contains(issue.StatusId));
        }

        if (input.PriorityIds is { Length: > 0 } priorityIds)
        {
            query = query.Where(issue => priorityIds.Contains(issue.PriorityId));
        }

        if (input.AssigneeIds is { Length: > 0 } assigneeIds)
        {
            query = query.Where(issue => issue.AssigneeId != null && assigneeIds.Contains(issue.AssigneeId.Value));
        }

        if (input.ReporterIds is { Length: > 0 } reporterIds)
        {
            query = query.Where(issue => issue.ReporterId != null && reporterIds.Contains(issue.ReporterId.Value));
        }

        if (input.ParentIds is { Length: > 0 } parentIds)
        {
            query = query.Where(issue => issue.ParentId != null && parentIds.Contains(issue.ParentId.Value));
        }

        if (input.CreatedFrom is { } createdFrom)
        {
            query = query.Where(issue => issue.CreationTime >= createdFrom);
        }

        if (input.CreatedTo is { } createdTo)
        {
            query = query.Where(issue => issue.CreationTime <= createdTo);
        }

        if (input.UpdatedFrom is { } updatedFrom)
        {
            query = query.Where(issue => (issue.LastModificationTime ?? issue.CreationTime) >= updatedFrom);
        }

        if (input.UpdatedTo is { } updatedTo)
        {
            query = query.Where(issue => (issue.LastModificationTime ?? issue.CreationTime) <= updatedTo);
        }

        return query;
    }

    private async Task<Issue?> FindByKeyAsync(string key)
    {
        var match = IssueKeyPattern().Match(key);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out var number))
        {
            return null;
        }

        var project = await projectAccess.FindRefByKeyAsync(match.Groups[1].Value);
        return project is null
            ? null
            : await issues.FirstOrDefaultAsync(issue => issue.ProjectId == project.Id && issue.Number == number);
    }

    private async Task<string?> GetLastRankAsync(Guid projectId)
    {
        var query = await issues.GetQueryableAsync();
        return await query.Where(issue => issue.ProjectId == projectId).MaxAsync(issue => (string?)issue.Rank);
    }

    private async Task<Guid> GetDefaultPriorityIdAsync()
    {
        var list = await priorities.GetListAsync();
        return list.Single(priority => priority.Name == IssuesDataSeedContributor.DefaultPriority).Id;
    }

    private async Task EnsureParentIsValidAsync(Guid projectId, IssueType type, Guid? parentId)
    {
        if (parentId is null)
        {
            if (type.Hierarchy == IssueHierarchy.Subtask)
            {
                throw new BusinessException(IssueErrors.ParentRequired);
            }

            return;
        }

        var parent = await issues.FindAsync(parentId.Value);
        if (parent is null || parent.ProjectId != projectId)
        {
            throw new BusinessException(IssueErrors.ProjectMismatch);
        }

        var parentType = await types.GetAsync(parent.TypeId);
        if ((int)parentType.Hierarchy != (int)type.Hierarchy + 1)
        {
            throw new BusinessException(IssueErrors.InvalidHierarchy);
        }
    }

    private static void EnsureDates(DateOnly? start, DateOnly? due)
    {
        if (start.HasValue && due.HasValue && due < start)
        {
            throw new BusinessException(IssueErrors.DueBeforeStart);
        }
    }

    /// <summary>Same path as the update API: one activity row per real change.</summary>
    public async Task SetFieldForWorkflowAsync(Issue issue, string field, JsonElement value)
    {
        var change = await ApplyFieldAsync(issue, field, value);
        if (change is not null)
        {
            await activities.InsertAsync(new IssueActivity(
                GuidGenerator.Create(), issue.Id, currentActor.Actor, field, change.Value.Old, change.Value.New, clock.Now));
            await issues.UpdateAsync(issue, autoSave: true);
        }
    }

    private async Task<(string? Old, string? New)?> ApplyFieldAsync(Issue issue, string field, JsonElement value)
    {
        switch (field)
        {
            case IssueFieldKeys.Title:
                var title = ReadString(value)?.Trim();
                if (string.IsNullOrEmpty(title))
                {
                    throw new BusinessException(IssueErrors.TitleRequired);
                }

                return Set(issue.Title, title, next => issue.Title = next!);

            case IssueFieldKeys.Description:
                var (html, text) = sanitizer.Clean(ReadString(value));
                await richTextFiles.SyncAsync(issue.ProjectId, issue.Id, html);
                return Set(issue.Description, html, next => issue.SetDescription(next!, text));

            case IssueFieldKeys.Assignee:
                var assignee = ReadGuid(value);
                return Set(issue.AssigneeId?.ToString(), assignee?.ToString(), _ =>
                {
                    issue.AssigneeId = assignee;
                    issue.AssigneeType = assignee.HasValue ? ActorType.User : null;
                });

            case IssueFieldKeys.Reporter:
                var reporter = ReadGuid(value);
                return Set(issue.ReporterId?.ToString(), reporter?.ToString(), _ => issue.ReporterId = reporter);

            case IssueFieldKeys.Priority:
                var priorityId = ReadGuid(value) ?? throw InvalidValue(field);
                _ = await priorities.GetAsync(priorityId);
                return Set(issue.PriorityId.ToString(), priorityId.ToString(), _ => issue.PriorityId = priorityId);

            case IssueFieldKeys.Type:
                var typeId = ReadGuid(value) ?? throw InvalidValue(field);
                var type = await types.FirstOrDefaultAsync(item => item.Id == typeId && item.ProjectId == issue.ProjectId)
                    ?? throw new BusinessException(IssueErrors.ProjectMismatch);
                await EnsureParentIsValidAsync(issue.ProjectId, type, issue.ParentId);
                await EnsureChildrenFitAsync(issue, type);
                return Set((await types.GetAsync(issue.TypeId)).Name, type.Name, _ => issue.TypeId = typeId);

            case IssueFieldKeys.Parent:
                var parent = ReadGuid(value);
                await EnsureParentIsValidAsync(issue.ProjectId, await types.GetAsync(issue.TypeId), parent);
                return Set(issue.ParentId?.ToString(), parent?.ToString(), _ => issue.ParentId = parent);

            case IssueFieldKeys.StoryPoints:
                var points = value.ValueKind == JsonValueKind.Null ? (decimal?)null : ReadDecimal(value);
                return Set(issue.StoryPoints?.ToString(), points?.ToString(), _ => issue.StoryPoints = points);

            case IssueFieldKeys.StartDate:
                var start = ReadDate(value);
                return Set(issue.StartDate?.ToString("O"), start?.ToString("O"), _ => issue.StartDate = start);

            case IssueFieldKeys.DueDate:
                var due = ReadDate(value);
                return Set(issue.DueDate?.ToString("O"), due?.ToString("O"), _ => issue.DueDate = due);

            case IssueFieldKeys.Labels:
                var names = value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
                    : throw InvalidValue(field);
                var before = await SetLabelsAsync(issue, names);
                var oldText = string.Join(", ", before);
                var newText = string.Join(", ", names.Select(name => name.Trim()).Where(name => name.Length > 0).Distinct().Order());
                return oldText == newText ? null : (oldText, newText);

            default:
                throw new BusinessException(IssueErrors.UnknownField).WithData("field", field);
        }
    }

    private async Task EnsureChildrenFitAsync(Issue issue, IssueType newType)
    {
        var hasChildren = await issues.AnyAsync(child => child.ParentId == issue.Id);
        if (hasChildren && newType.Hierarchy == IssueHierarchy.Subtask)
        {
            throw new BusinessException(IssueErrors.InvalidHierarchy);
        }

        var current = await types.GetAsync(issue.TypeId);
        if (hasChildren && current.Hierarchy != newType.Hierarchy)
        {
            throw new BusinessException(IssueErrors.InvalidHierarchy);
        }
    }

    private async Task<List<string>> SetLabelsAsync(Issue issue, string[] names)
    {
        var wanted = names.Select(name => name.Trim()).Where(name => name.Length > 0).Distinct().ToList();
        var links = await issueLabels.GetListAsync(link => link.IssueId == issue.Id);
        var currentIds = links.Select(link => link.LabelId).ToList();
        var current = await labels.GetListAsync(label => currentIds.Contains(label.Id));
        var before = current.Select(label => label.Name).Order().ToList();

        foreach (var link in links)
        {
            if (!wanted.Contains(current.Single(label => label.Id == link.LabelId).Name))
            {
                await issueLabels.DeleteAsync(link);
            }
        }

        foreach (var name in wanted.Except(before))
        {
            var label = await labels.FirstOrDefaultAsync(item => item.ProjectId == issue.ProjectId && item.Name == name)
                ?? await labels.InsertAsync(new Label(GuidGenerator.Create(), issue.ProjectId, name), autoSave: true);
            await issueLabels.InsertAsync(new IssueLabel(GuidGenerator.Create(), issue.Id, label.Id));
        }

        return before;
    }

    private static (string? Old, string? New)? Set(string? current, string? next, Action<string?> apply)
    {
        if (current == next)
        {
            return null;
        }

        apply(next);
        return (current, next);
    }

    private static string? ReadString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        _ => throw InvalidValue("string"),
    };

    private static Guid? ReadGuid(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String when value.TryGetGuid(out var id) => id,
        _ => throw InvalidValue("id"),
    };

    private static decimal ReadDecimal(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number >= 0
            ? number
            : throw InvalidValue("number");

    private static DateOnly? ReadDate(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String when DateOnly.TryParse(value.GetString(), out var date) => date,
        _ => throw InvalidValue("date"),
    };

    private static BusinessException InvalidValue(string expected) =>
        new BusinessException(IssueErrors.InvalidFieldValue).WithData("expected", expected);
}
