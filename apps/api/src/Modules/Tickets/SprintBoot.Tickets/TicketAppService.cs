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

namespace SprintBoot.Tickets;

[Authorize]
[Volo.Abp.DependencyInjection.ExposeServices(typeof(ITicketAppService), typeof(TicketAppService))]
public partial class TicketAppService(
    IRepository<Ticket, Guid> tickets,
    IRepository<TicketType, Guid> types,
    IRepository<Priority, Guid> priorities,
    IRepository<Label, Guid> labels,
    IRepository<TicketLabel, Guid> ticketLabels,
    IRepository<TicketActivity, Guid> activities,
    IProjectAccess projectAccess,
    ITicketFieldPolicy fieldPolicy,
    IWorkflowRuntime workflowRuntime,
    IStatusLookup statusLookup,
    IUserDirectory userDirectory,
    ICurrentActor currentActor,
    IClock clock,
    IDataFilter dataFilter,
    IDistributedEventBus eventBus,
    TicketMapper mapper,
    WatcherStore watcherStore,
    RichTextSanitizer sanitizer,
    RichTextFileStore richTextFiles,
    TicketSorting sorting,
    ITicketSprintNames sprintNames)
    : ApplicationService, ITicketAppService
{
    [GeneratedRegex("^([A-Z][A-Z0-9]{1,9})-([0-9]+)$")]
    private static partial Regex TicketKeyPattern();

    public async Task<List<TicketTypeDto>> GetTypesAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);
        var list = await types.GetListAsync(type => type.ProjectId == projectId);
        return [.. list.OrderByDescending(type => type.Hierarchy).ThenBy(type => type.Name).Select(TicketMapper.ToDto)];
    }

    public async Task<List<PriorityDto>> GetPrioritiesAsync()
    {
        var list = await priorities.GetListAsync();
        return [.. list.OrderBy(priority => priority.Order).Select(TicketMapper.ToDto)];
    }

    public async Task<TicketDto> GetByKeyAsync(string key)
    {
        var ticket = await FindByKeyAsync(key) ?? throw new EntityNotFoundException(typeof(Ticket), key);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);
        return await mapper.ToDtoAsync(ticket);
    }

    public async Task<TicketDto> CreateAsync(CreateTicketInput input)
    {
        await projectAccess.CheckAsync(input.ProjectId, ProjectPermissions.EditTickets);

        var type = await types.FirstOrDefaultAsync(item => item.Id == input.TypeId && item.ProjectId == input.ProjectId)
            ?? throw new BusinessException(TicketErrors.ProjectMismatch);
        await EnsureParentIsValidAsync(input.ProjectId, type, input.ParentId);
        EnsureDates(input.StartDate, input.DueDate);

        var priorityId = input.PriorityId ?? await GetDefaultPriorityIdAsync();
        var initial = await workflowRuntime.GetInitialStateAsync(input.ProjectId, type.Id);
        var number = await projectAccess.NextTicketNumberAsync(input.ProjectId);

        var actor = currentActor.Actor;
        var ticket = new Ticket(GuidGenerator.Create(), input.ProjectId, number, type.Id, input.Title.Trim(), initial.StatusId, initial.VersionId, priorityId)
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
        ticket.SetDescription(html, text);
        ticket.SetRank(RankMath.Between(await GetLastRankAsync(input.ProjectId), null));

        await tickets.InsertAsync(ticket, autoSave: true);
        await SetLabelsAsync(ticket, input.Labels ?? []);
        await richTextFiles.SyncAsync(ticket.ProjectId, ticket.Id, ticket.Description);
        await watcherStore.EnsureAsync(ticket.Id, ticket.ReporterId, ticket.AssigneeId);
        await eventBus.PublishAsync(
            new TicketCreatedEto
            {
                EventId = GuidGenerator.Create(),
                TicketId = ticket.Id,
                ProjectId = ticket.ProjectId,
                ActorId = actor.Id,
                Mentioned = RichTextSanitizer.Mentions(ticket.Description),
            },
            useOutbox: true);

        return await mapper.ToDtoAsync(ticket);
    }

    public async Task<TicketDto> UpdateAsync(Guid id, UpdateTicketInput input)
    {
        var ticket = await tickets.GetAsync(id);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.EditTickets);

        if (ticket.ConcurrencyStamp != input.ConcurrencyStamp)
        {
            throw new AbpDbConcurrencyException("The ticket was changed by someone else.");
        }

        var changed = new List<string>();
        foreach (var (field, value) in input.Fields)
        {
            if (field == TicketFieldKeys.Status)
            {
                throw new BusinessException(TicketErrors.FieldNotWritable).WithData("field", field);
            }

            if (!await fieldPolicy.CanWriteAsync(ticket.ProjectId, field))
            {
                throw new BusinessException(TicketErrors.FieldNotWritable).WithData("field", field);
            }

            var change = await ApplyFieldAsync(ticket, field, value);
            if (change is not null)
            {
                changed.Add(field);
                await activities.InsertAsync(new TicketActivity(
                    GuidGenerator.Create(), ticket.Id, currentActor.Actor, field, change.Value.Old, change.Value.New, clock.Now));
            }
        }

        EnsureDates(ticket.StartDate, ticket.DueDate);

        if (changed.Count > 0)
        {
            await tickets.UpdateAsync(ticket, autoSave: true);
            await watcherStore.EnsureAsync(ticket.Id, ticket.AssigneeId);
            await eventBus.PublishAsync(
                new TicketUpdatedEto
                {
                    EventId = GuidGenerator.Create(),
                    TicketId = ticket.Id,
                    ProjectId = ticket.ProjectId,
                    ActorId = currentActor.Actor.Id,
                    Fields = [.. changed],
                    Mentioned = changed.Contains(TicketFieldKeys.Description) ? RichTextSanitizer.Mentions(ticket.Description) : [],
                },
                useOutbox: true);
        }

        return await mapper.ToDtoAsync(ticket);
    }

    public async Task DeleteAsync(Guid id)
    {
        var ticket = await tickets.GetAsync(id);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);

        var canDeleteAny = await projectAccess.IsGrantedAsync(ticket.ProjectId, ProjectPermissions.DeleteAnyTicket);
        var canDeleteOwn = ticket.CreatorId == CurrentUser.Id
            && await projectAccess.IsGrantedAsync(ticket.ProjectId, ProjectPermissions.DeleteOwnTicket);
        if (!canDeleteAny && !canDeleteOwn)
        {
            throw new Volo.Abp.Authorization.AbpAuthorizationException("Missing permission to delete this ticket");
        }

        var subtasks = await tickets.GetListAsync(child => child.ParentId == id);
        await tickets.DeleteAsync(ticket, autoSave: true);
        await tickets.DeleteManyAsync(subtasks);
    }

    public async Task<TicketSearchResultDto> GetSearchAsync(TicketSearchInput input)
    {
        if (input.ProjectId is { } projectId)
        {
            await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);
        }

        var visible = await projectAccess.GetVisibleProjectIdsAsync();
        var query = await tickets.GetQueryableAsync();

        if (visible is not null)
        {
            query = query.Where(ticket => visible.Contains(ticket.ProjectId));
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
                query = query.Where(ticket => ticket.Id == exact.Id);
            }
            else
            {
                var pattern = $"%{text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
                query = query.Where(ticket =>
                    ticket.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("simple", text))
                    || EF.Functions.ILike(ticket.Title, pattern));
            }
        }

        query = ApplyFilters(query, input);
        if (input.SprintIds is { Length: > 0 } sprintIds)
        {
            // Sprints live in the Agile module, so their tickets come back as ids through its contract.
            var inSprints = (await sprintNames.GetTicketIdsAsync(sprintIds)).ToList();
            query = query.Where(ticket => inSprints.Contains(ticket.Id));
        }

        if (input.Labels is { Length: > 0 } names)
        {
            var labelQuery = await labels.GetQueryableAsync();
            var linkQuery = await ticketLabels.GetQueryableAsync();
            var labelledIds = from link in linkQuery
                              join label in labelQuery on link.LabelId equals label.Id
                              where names.Contains(label.Name)
                              select link.TicketId;
            query = query.Where(ticket => labelledIds.Contains(ticket.Id));
        }

        var total = await query.LongCountAsync();
        var page = await (await sorting.ApplyAsync(query, input.Sorting))
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToListAsync();

        return new TicketSearchResultDto
        {
            TotalCount = total,
            Items = await mapper.ToSummariesAsync(page),
            ExactKey = exactKey,
        };
    }

    public async Task<List<string>> GetLabelsAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);
        var list = await labels.GetListAsync(label => label.ProjectId == projectId);
        return [.. list.Select(label => label.Name).Order()];
    }

    public async Task<List<EpicRowDto>> GetEpicsAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);

        var epicTypes = await types.GetListAsync(type => type.ProjectId == projectId && type.Hierarchy == TicketHierarchy.Epic);
        var epicTypeIds = epicTypes.Select(type => type.Id).ToList();
        var epics = await tickets.GetListAsync(ticket => ticket.ProjectId == projectId && epicTypeIds.Contains(ticket.TypeId));
        var epicIds = epics.Select(epic => epic.Id).ToList();
        var children = await tickets.GetListAsync(ticket => ticket.ParentId != null && epicIds.Contains(ticket.ParentId.Value));

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
                Priority = TicketMapper.ToDto(priorityMap[epic.PriorityId]),
                DueDate = epic.DueDate,
                Lead = epic.AssigneeId is { } lead ? leads.GetValueOrDefault(lead) : null,
                ConcurrencyStamp = epic.ConcurrencyStamp,
                Total = own.Count,
                Done = own.Count(child => statuses[child.StatusId].Category == StatusCategory.Done),
                InProgress = own.Count(child => statuses[child.StatusId].Category == StatusCategory.InProgress),
            };
        })];
    }

    public async Task<List<TicketActivityDto>> GetActivityAsync(Guid id)
    {
        var ticket = await tickets.GetAsync(id);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);

        var list = await activities.GetListAsync(activity => activity.TicketId == id);
        var names = await ActivityNamesAsync(list);
        var users = await userDirectory.GetByIdsAsync(list.Select(activity => activity.ActorId));
        string? Show(string? value) => value is not null && Guid.TryParse(value, out var key) && names.TryGetValue(key, out var name) ? name : value;
        return [.. list.OrderByDescending(activity => activity.CreatedAt).Select(activity => new TicketActivityDto
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
    /// History stores ids for fields that point at people, priorities or tickets, so a rename shows
    /// everywhere; readers get the current name instead.
    /// </summary>
    private async Task<Dictionary<Guid, string>> ActivityNamesAsync(List<TicketActivity> list)
    {
        HashSet<Guid> IdsOf(params string[] fields) =>
            [.. list.Where(activity => fields.Contains(activity.Field))
                .SelectMany(activity => new[] { activity.OldValue, activity.NewValue })
                .Select(value => Guid.TryParse(value, out var key) ? key : Guid.Empty)
                .Where(key => key != Guid.Empty)];

        var names = new Dictionary<Guid, string>();
        var people = IdsOf(TicketFieldKeys.Assignee, TicketFieldKeys.Reporter);
        foreach (var (key, user) in await userDirectory.GetByIdsAsync(people))
        {
            names[key] = user.Name;
        }

        var priorityIds = IdsOf(TicketFieldKeys.Priority);
        foreach (var priority in await priorities.GetListAsync(priority => priorityIds.Contains(priority.Id)))
        {
            names[priority.Id] = priority.Name;
        }

        // A parent may have been deleted since; its key still says which one it was.
        var parentIds = IdsOf(TicketFieldKeys.Parent);
        using (dataFilter.Disable<ISoftDelete>())
        {
            var parents = await tickets.GetListAsync(ticket => parentIds.Contains(ticket.Id));
            var projects = await projectAccess.GetRefsAsync(parents.Select(parent => parent.ProjectId));
            foreach (var parent in parents)
            {
                names[parent.Id] = projects.TryGetValue(parent.ProjectId, out var project) ? $"{project.Key}-{parent.Number} {parent.Title}" : parent.Title;
            }
        }

        return names;
    }

    [Authorize(SprintBootPermissions.RestoreTickets)]
    public async Task<PagedResultDto<DeletedTicketDto>> GetDeletedListAsync(PagedResultRequestDto input)
    {
        using (dataFilter.Disable<ISoftDelete>())
        {
            var query = (await tickets.GetQueryableAsync()).Where(ticket => ticket.IsDeleted);
            var subtaskIds = query.Where(ticket => ticket.ParentId != null).Select(ticket => ticket.Id);
            var roots = query.Where(ticket => !subtaskIds.Contains(ticket.Id) || !query.Any(parent => parent.Id == ticket.ParentId));

            var total = await roots.LongCountAsync();
            var page = await roots.OrderByDescending(ticket => ticket.DeletionTime)
                .Skip(input.SkipCount).Take(input.MaxResultCount).ToListAsync();
            var ids = page.Select(ticket => ticket.Id).ToList();
            var childCounts = await query.Where(ticket => ticket.ParentId != null && ids.Contains(ticket.ParentId.Value))
                .GroupBy(ticket => ticket.ParentId!.Value)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.Key, item => item.Count);

            var projects = await projectAccess.GetRefsAsync(page.Select(ticket => ticket.ProjectId));
            var users = await userDirectory.GetByIdsAsync(page.Where(i => i.DeleterId.HasValue).Select(i => i.DeleterId!.Value));
            return new PagedResultDto<DeletedTicketDto>(total, [.. page.Select(ticket => new DeletedTicketDto
            {
                Id = ticket.Id,
                Key = $"{projects[ticket.ProjectId].Key}-{ticket.Number}",
                Title = ticket.Title,
                ProjectName = projects[ticket.ProjectId].Name,
                SubtaskCount = childCounts.GetValueOrDefault(ticket.Id),
                DeletedBy = ticket.DeleterId is { } deleter ? users.GetValueOrDefault(deleter) : null,
                DeletedAt = ticket.DeletionTime!.Value,
            })]);
        }
    }

    [Authorize(SprintBootPermissions.RestoreTickets)]
    public async Task RestoreAsync(Guid id)
    {
        using (dataFilter.Disable<ISoftDelete>())
        {
            var ticket = await tickets.GetAsync(id);
            var deletedAt = ticket.DeletionTime;
            // Subtasks removed earlier on their own stay deleted.
            var subtasks = await tickets.GetListAsync(child => child.ParentId == id && child.IsDeleted && child.DeletionTime >= deletedAt);
            foreach (var item in subtasks.Append(ticket))
            {
                item.IsDeleted = false;
                item.DeletionTime = null;
                item.DeleterId = null;
                await tickets.UpdateAsync(item);
            }
        }
    }

    private static IQueryable<Ticket> ApplyFilters(IQueryable<Ticket> query, TicketSearchInput input)
    {
        if (input.ProjectId is { } projectId)
        {
            query = query.Where(ticket => ticket.ProjectId == projectId);
        }

        if (input.TypeIds is { Length: > 0 } typeIds)
        {
            query = query.Where(ticket => typeIds.Contains(ticket.TypeId));
        }

        if (input.StatusIds is { Length: > 0 } statusIds)
        {
            query = query.Where(ticket => statusIds.Contains(ticket.StatusId));
        }

        if (input.PriorityIds is { Length: > 0 } priorityIds)
        {
            query = query.Where(ticket => priorityIds.Contains(ticket.PriorityId));
        }

        if (input.AssigneeIds is { Length: > 0 } assigneeIds)
        {
            query = query.Where(ticket => ticket.AssigneeId != null && assigneeIds.Contains(ticket.AssigneeId.Value));
        }

        if (input.ReporterIds is { Length: > 0 } reporterIds)
        {
            query = query.Where(ticket => ticket.ReporterId != null && reporterIds.Contains(ticket.ReporterId.Value));
        }

        if (input.ParentIds is { Length: > 0 } parentIds)
        {
            query = query.Where(ticket => ticket.ParentId != null && parentIds.Contains(ticket.ParentId.Value));
        }

        if (input.CreatedFrom is { } createdFrom)
        {
            query = query.Where(ticket => ticket.CreationTime >= createdFrom);
        }

        if (input.CreatedTo is { } createdTo)
        {
            query = query.Where(ticket => ticket.CreationTime <= createdTo);
        }

        if (input.UpdatedFrom is { } updatedFrom)
        {
            query = query.Where(ticket => (ticket.LastModificationTime ?? ticket.CreationTime) >= updatedFrom);
        }

        if (input.UpdatedTo is { } updatedTo)
        {
            query = query.Where(ticket => (ticket.LastModificationTime ?? ticket.CreationTime) <= updatedTo);
        }

        return query;
    }

    private async Task<Ticket?> FindByKeyAsync(string key)
    {
        var match = TicketKeyPattern().Match(key);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out var number))
        {
            return null;
        }

        var project = await projectAccess.FindRefByKeyAsync(match.Groups[1].Value);
        return project is null
            ? null
            : await tickets.FirstOrDefaultAsync(ticket => ticket.ProjectId == project.Id && ticket.Number == number);
    }

    private async Task<string?> GetLastRankAsync(Guid projectId)
    {
        var query = await tickets.GetQueryableAsync();
        return await query.Where(ticket => ticket.ProjectId == projectId).MaxAsync(ticket => (string?)ticket.Rank);
    }

    private async Task<Guid> GetDefaultPriorityIdAsync()
    {
        var list = await priorities.GetListAsync();
        return list.Single(priority => priority.Name == TicketsDataSeedContributor.DefaultPriority).Id;
    }

    private async Task EnsureParentIsValidAsync(Guid projectId, TicketType type, Guid? parentId)
    {
        if (parentId is null)
        {
            if (type.Hierarchy == TicketHierarchy.Subtask)
            {
                throw new BusinessException(TicketErrors.ParentRequired);
            }

            return;
        }

        var parent = await tickets.FindAsync(parentId.Value);
        if (parent is null || parent.ProjectId != projectId)
        {
            throw new BusinessException(TicketErrors.ProjectMismatch);
        }

        var parentType = await types.GetAsync(parent.TypeId);
        if ((int)parentType.Hierarchy != (int)type.Hierarchy + 1)
        {
            throw new BusinessException(TicketErrors.InvalidHierarchy);
        }
    }

    private static void EnsureDates(DateOnly? start, DateOnly? due)
    {
        if (start.HasValue && due.HasValue && due < start)
        {
            throw new BusinessException(TicketErrors.DueBeforeStart);
        }
    }

    /// <summary>Same path as the update API: one activity row per real change.</summary>
    public async Task SetFieldForWorkflowAsync(Ticket ticket, string field, JsonElement value)
    {
        var change = await ApplyFieldAsync(ticket, field, value);
        if (change is not null)
        {
            await activities.InsertAsync(new TicketActivity(
                GuidGenerator.Create(), ticket.Id, currentActor.Actor, field, change.Value.Old, change.Value.New, clock.Now));
            await tickets.UpdateAsync(ticket, autoSave: true);
        }
    }

    private async Task<(string? Old, string? New)?> ApplyFieldAsync(Ticket ticket, string field, JsonElement value)
    {
        switch (field)
        {
            case TicketFieldKeys.Title:
                var title = ReadString(value)?.Trim();
                if (string.IsNullOrEmpty(title))
                {
                    throw new BusinessException(TicketErrors.TitleRequired);
                }

                return Set(ticket.Title, title, next => ticket.Title = next!);

            case TicketFieldKeys.Description:
                var (html, text) = sanitizer.Clean(ReadString(value));
                await richTextFiles.SyncAsync(ticket.ProjectId, ticket.Id, html);
                return Set(ticket.Description, html, next => ticket.SetDescription(next!, text));

            case TicketFieldKeys.Assignee:
                var assignee = ReadGuid(value);
                return Set(ticket.AssigneeId?.ToString(), assignee?.ToString(), _ =>
                {
                    ticket.AssigneeId = assignee;
                    ticket.AssigneeType = assignee.HasValue ? ActorType.User : null;
                });

            case TicketFieldKeys.Reporter:
                var reporter = ReadGuid(value);
                return Set(ticket.ReporterId?.ToString(), reporter?.ToString(), _ => ticket.ReporterId = reporter);

            case TicketFieldKeys.Priority:
                var priorityId = ReadGuid(value) ?? throw InvalidValue(field);
                _ = await priorities.GetAsync(priorityId);
                return Set(ticket.PriorityId.ToString(), priorityId.ToString(), _ => ticket.PriorityId = priorityId);

            case TicketFieldKeys.Type:
                var typeId = ReadGuid(value) ?? throw InvalidValue(field);
                var type = await types.FirstOrDefaultAsync(item => item.Id == typeId && item.ProjectId == ticket.ProjectId)
                    ?? throw new BusinessException(TicketErrors.ProjectMismatch);
                await EnsureParentIsValidAsync(ticket.ProjectId, type, ticket.ParentId);
                await EnsureChildrenFitAsync(ticket, type);
                return Set((await types.GetAsync(ticket.TypeId)).Name, type.Name, _ => ticket.TypeId = typeId);

            case TicketFieldKeys.Parent:
                var parent = ReadGuid(value);
                await EnsureParentIsValidAsync(ticket.ProjectId, await types.GetAsync(ticket.TypeId), parent);
                return Set(ticket.ParentId?.ToString(), parent?.ToString(), _ => ticket.ParentId = parent);

            case TicketFieldKeys.StoryPoints:
                var points = value.ValueKind == JsonValueKind.Null ? (decimal?)null : ReadDecimal(value);
                return Set(ticket.StoryPoints?.ToString(), points?.ToString(), _ => ticket.StoryPoints = points);

            case TicketFieldKeys.StartDate:
                var start = ReadDate(value);
                return Set(ticket.StartDate?.ToString("O"), start?.ToString("O"), _ => ticket.StartDate = start);

            case TicketFieldKeys.DueDate:
                var due = ReadDate(value);
                return Set(ticket.DueDate?.ToString("O"), due?.ToString("O"), _ => ticket.DueDate = due);

            case TicketFieldKeys.Labels:
                var names = value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
                    : throw InvalidValue(field);
                var before = await SetLabelsAsync(ticket, names);
                var oldText = string.Join(", ", before);
                var newText = string.Join(", ", names.Select(name => name.Trim()).Where(name => name.Length > 0).Distinct().Order());
                return oldText == newText ? null : (oldText, newText);

            default:
                throw new BusinessException(TicketErrors.UnknownField).WithData("field", field);
        }
    }

    private async Task EnsureChildrenFitAsync(Ticket ticket, TicketType newType)
    {
        var hasChildren = await tickets.AnyAsync(child => child.ParentId == ticket.Id);
        if (hasChildren && newType.Hierarchy == TicketHierarchy.Subtask)
        {
            throw new BusinessException(TicketErrors.InvalidHierarchy);
        }

        var current = await types.GetAsync(ticket.TypeId);
        if (hasChildren && current.Hierarchy != newType.Hierarchy)
        {
            throw new BusinessException(TicketErrors.InvalidHierarchy);
        }
    }

    private async Task<List<string>> SetLabelsAsync(Ticket ticket, string[] names)
    {
        var wanted = names.Select(name => name.Trim()).Where(name => name.Length > 0).Distinct().ToList();
        var links = await ticketLabels.GetListAsync(link => link.TicketId == ticket.Id);
        var currentIds = links.Select(link => link.LabelId).ToList();
        var current = await labels.GetListAsync(label => currentIds.Contains(label.Id));
        var before = current.Select(label => label.Name).Order().ToList();

        foreach (var link in links)
        {
            if (!wanted.Contains(current.Single(label => label.Id == link.LabelId).Name))
            {
                await ticketLabels.DeleteAsync(link);
            }
        }

        foreach (var name in wanted.Except(before))
        {
            var label = await labels.FirstOrDefaultAsync(item => item.ProjectId == ticket.ProjectId && item.Name == name)
                ?? await labels.InsertAsync(new Label(GuidGenerator.Create(), ticket.ProjectId, name), autoSave: true);
            await ticketLabels.InsertAsync(new TicketLabel(GuidGenerator.Create(), ticket.Id, label.Id));
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
        new BusinessException(TicketErrors.InvalidFieldValue).WithData("expected", expected);
}
