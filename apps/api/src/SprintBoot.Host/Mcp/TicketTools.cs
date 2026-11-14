using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SprintBoot.Agile;
using SprintBoot.Tickets;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.Caching;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Users;

namespace SprintBoot.Host.Mcp;

public class TicketDraft
{
    [Description("Ticket title.")]
    public required string Title { get; set; }

    [Description("Ticket type name, such as Task, Story, Bug, Epic or Subtask. Defaults to Task.")]
    public string? Type { get; set; }

    [Description("Description in Markdown.")]
    public string? Description { get; set; }

    [Description("Priority name, such as High.")]
    public string? Priority { get; set; }

    [Description("Assignee: me, an email, a name or a user id.")]
    public string? Assignee { get; set; }

    [Description("Key of an existing parent ticket, such as BANK-3.")]
    public string? Parent { get; set; }

    [Description("create_tickets only: the index (from 0) of an earlier item in the same batch to use as parent.")]
    public int? ParentRef { get; set; }

    public List<string>? Labels { get; set; }

    public decimal? StoryPoints { get; set; }

    [Description("Date like 2026-11-30.")]
    public string? StartDate { get; set; }

    [Description("Date like 2026-11-30.")]
    public string? DueDate { get; set; }

    [Description("Sprint name, active for the running sprint, or backlog.")]
    public string? Sprint { get; set; }
}

public class IdempotentCreation
{
    public string Key { get; set; } = string.Empty;
}

/// <summary>Ticket tools. Thin adapters: the app services decide, with the token owner's permissions.</summary>
[McpServerToolType]
public class TicketTools(
    McpRunner runner,
    McpResolver resolver,
    IProjectAppService projects,
    ITicketAppService tickets,
    ICommentAppService comments,
    ITransitionAppService transitions,
    ISprintAppService sprints,
    ITicketLinkAppService links,
    IAbpDistributedLock locks,
    IDistributedCache<IdempotentCreation> created,
    ICurrentUser currentUser)
{
    private const int MaxBatch = 20;
    private const int MaxBulkUpdate = 50;

    private static readonly string[] Clearable =
        [TicketFieldKeys.Description, TicketFieldKeys.Assignee, TicketFieldKeys.Parent, TicketFieldKeys.Labels,
         TicketFieldKeys.StoryPoints, TicketFieldKeys.StartDate, TicketFieldKeys.DueDate];

    private static readonly string[] FieldKeys =
        [TicketFieldKeys.Title, TicketFieldKeys.Description, TicketFieldKeys.Type, TicketFieldKeys.Priority, TicketFieldKeys.Assignee,
         TicketFieldKeys.Reporter, TicketFieldKeys.Parent, TicketFieldKeys.Labels, TicketFieldKeys.StoryPoints, TicketFieldKeys.StartDate,
         TicketFieldKeys.DueDate];

    [McpServerTool(Name = "search_tickets", ReadOnly = true), Description(
        "Finds tickets, newest change first. Filters combine with AND; values inside one filter combine with OR. " +
        "Names are those get_space_fields lists. Pages with offset and limit.")]
    public Task<string> SearchTickets(
        [Description("Words to look for in the key, title or description.")] string? text = null,
        [Description("Space key, such as BANK.")] string? space = null,
        [Description("Ticket type names.")] List<string>? types = null,
        [Description("Status names.")] List<string>? statuses = null,
        [Description("Priority names.")] List<string>? priorities = null,
        [Description("Assignees: me, emails, names or ids.")] List<string>? assignees = null,
        [Description("Reporters: me, emails, names or ids.")] List<string>? reporters = null,
        [Description("Labels; a ticket matches when it has any of them.")] List<string>? labels = null,
        [Description("Parent ticket key, to list its children.")] string? parent = null,
        [Description("Created on or after, such as 2026-11-01.")] string? createdFrom = null,
        [Description("Created on or before.")] string? createdTo = null,
        [Description("Updated on or after.")] string? updatedFrom = null,
        [Description("Updated on or before.")] string? updatedTo = null,
        [Description("How many to skip, from nextOffset of the previous page.")] int offset = 0,
        [Description("How many to return, at most 50.")] int limit = 20) => runner.ReadAsync(async () =>
    {
        var project = space is null ? null : await resolver.SpaceAsync(space);
        var input = new TicketSearchInput
        {
            ProjectId = project?.Id,
            Text = text,
            SkipCount = Math.Max(0, offset),
            MaxResultCount = Math.Clamp(limit, 1, 50),
            Labels = labels is { Count: > 0 } ? [.. labels] : null,
            CreatedFrom = createdFrom is null ? null : McpResolver.Time(createdFrom, "createdFrom"),
            CreatedTo = createdTo is null ? null : EndOfDay(createdTo, "createdTo"),
            UpdatedFrom = updatedFrom is null ? null : McpResolver.Time(updatedFrom, "updatedFrom"),
            UpdatedTo = updatedTo is null ? null : EndOfDay(updatedTo, "updatedTo"),
        };

        if (types is { Count: > 0 })
        {
            var spaces = project is null ? await projects.GetListAsync() : [project];
            var known = new List<TicketTypeDto>();
            foreach (var item in spaces)
            {
                known.AddRange(await resolver.TypesAsync(item.Id));
            }

            input.TypeIds = [.. types.SelectMany(name => Matching(known, name, type => type.Name, type => type.Id, "ticket type"))];
        }

        if (statuses is { Count: > 0 })
        {
            var known = await resolver.StatusesAsync(project?.Id);
            input.StatusIds = [.. statuses.SelectMany(name => Matching(known, name, status => status.Name, status => status.Id, "status"))];
        }

        if (priorities is { Count: > 0 })
        {
            var ids = new List<Guid>();
            foreach (var name in priorities)
            {
                ids.Add((await resolver.PriorityAsync(name)).Id);
            }

            input.PriorityIds = [.. ids];
        }

        if (assignees is { Count: > 0 })
        {
            input.AssigneeIds = [.. await UserIdsAsync(assignees)];
        }

        if (reporters is { Count: > 0 })
        {
            input.ReporterIds = [.. await UserIdsAsync(reporters)];
        }

        if (parent is not null)
        {
            input.ParentIds = [(await resolver.TicketAsync(parent)).Id];
        }

        var result = await tickets.GetSearchAsync(input);
        var next = input.SkipCount + result.Items.Count;
        return new
        {
            Total = result.TotalCount,
            Offset = input.SkipCount,
            NextOffset = next < result.TotalCount ? next : (int?)null,
            Tickets = result.Items.Select(ticket => new
            {
                ticket.Key,
                ticket.Title,
                Type = ticket.Type.Name,
                Status = ticket.Status.Name,
                Priority = ticket.Priority.Name,
                Assignee = McpViews.Person(ticket.Assignee),
                Sprint = ticket.SprintName,
                ticket.UpdatedAt,
            }),
        };
    });

    [McpServerTool(Name = "get_ticket", ReadOnly = true), Description(
        "Reads one ticket with every field, its description as Markdown, its children, its linked tickets and, by default, its comments.")]
    public Task<string> GetTicket(
        [Description("Ticket key, such as BANK-12.")] string key,
        [Description("markdown (default) or html, for the description and comments.")] string format = "markdown",
        [Description("Include the comment thread.")] bool includeComments = true) => runner.ReadAsync(async () =>
    {
        var html = format.Equals("html", StringComparison.OrdinalIgnoreCase);
        var ticket = await resolver.TicketAsync(key);
        var sprint = await sprints.GetForTicketAsync(ticket.Id);
        var thread = includeComments ? await comments.GetListAsync(ticket.Id) : null;
        var linked = await links.GetListAsync(ticket.Id);
        return new
        {
            ticket.Key,
            ticket.Title,
            Space = ticket.ProjectKey,
            Type = ticket.Type.Name,
            Status = ticket.Status.Name,
            StatusCategory = ticket.Status.Category.ToString(),
            Priority = ticket.Priority.Name,
            Assignee = McpViews.Person(ticket.Assignee),
            Reporter = McpViews.Person(ticket.Reporter),
            Parent = ticket.Parent is null ? null : McpViews.Ref(ticket.Parent),
            Children = ticket.Children.Count == 0 ? null : ticket.Children.Select(McpViews.Ref),
            // Each reads from this ticket: "blocks BANK-4". A ticket the caller cannot open shows no key.
            Links = linked.Count == 0 ? null : linked.Select(link => new { Relationship = link.Phrase, Ticket = link.Ticket is null ? null : McpViews.Ref(link.Ticket) }),
            ticket.Labels,
            ticket.StoryPoints,
            ticket.StartDate,
            ticket.DueDate,
            Sprint = sprint is null ? null : $"{sprint.Name} ({sprint.State})",
            TimeLogged = ticket.TotalMinutesLogged == 0 ? null : McpViews.Duration(ticket.TotalMinutesLogged),
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.ConcurrencyStamp,
            Description = html ? ticket.Description : RichTextMarkdown.ToMarkdown(ticket.Description),
            Comments = thread is null ? null
                : html ? thread.Select(comment => (object)new { comment.Id, comment.ParentId, Author = McpViews.Person(comment.Author), comment.Body, comment.CreatedAt }).ToList()
                : McpViews.Thread(thread),
        };
    });

    [McpServerTool(Name = "create_ticket", Destructive = false), Description(
        "Creates a ticket. Pass idempotencyKey when retrying, so a repeated call returns the first ticket instead of a copy.")]
    public async Task<string> CreateTicket(
        [Description("Space key.")] string space,
        [Description("Ticket title.")] string title,
        [Description("Ticket type name: Task, Story, Bug, Epic, Subtask, or another the space defines.")] string type = "Task",
        [Description("Description in Markdown. Mention people with @[Name](user:email).")] string? description = null,
        [Description("Priority name.")] string? priority = null,
        [Description("Assignee: me, an email, a name or a user id.")] string? assignee = null,
        [Description("Parent ticket key: the epic, or the ticket a subtask belongs to.")] string? parent = null,
        List<string>? labels = null,
        decimal? storyPoints = null,
        [Description("Date like 2026-11-30.")] string? startDate = null,
        [Description("Date like 2026-11-30.")] string? dueDate = null,
        [Description("Sprint name, or active for the running sprint.")] string? sprint = null,
        [Description("Any unique string for this creation; repeats within a day return the same ticket.")] string? idempotencyKey = null)
    {
        runner.EnsureWritable();
        var draft = new TicketDraft
        {
            Title = title, Type = type, Description = description, Priority = priority, Assignee = assignee,
            Parent = parent, Labels = labels, StoryPoints = storyPoints, StartDate = startDate, DueDate = dueDate, Sprint = sprint,
        };

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return await runner.WriteAsync(async () => Created(await CreateOneAsync(await resolver.SpaceAsync(space), draft, null), replayed: false));
        }

        var cacheKey = $"{currentUser.GetId()}:{idempotencyKey.Trim()}";
        await using var handle = await locks.TryAcquireAsync($"mcp-create:{cacheKey}", TimeSpan.FromSeconds(30))
            ?? throw new McpException("The same idempotencyKey is being used by another call. Retry shortly.");

        if (await created.GetAsync(cacheKey) is { } earlier)
        {
            return await runner.ReadAsync(async () => Created(await resolver.TicketAsync(earlier.Key), replayed: true));
        }

        string? newKey = null;
        var result = await runner.WriteAsync(async () =>
        {
            var ticket = await CreateOneAsync(await resolver.SpaceAsync(space), draft, null);
            newKey = ticket.Key;
            return Created(ticket, replayed: false);
        });

        // Only after the unit of work committed, so a replay never points at a rolled back ticket.
        await created.SetAsync(cacheKey, new IdempotentCreation { Key = newKey! }, new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) });
        return result;
    }

    [McpServerTool(Name = "create_tickets", Destructive = false), Description(
        "Creates up to 20 tickets in one space. Each item succeeds or fails on its own; an item can name an earlier one as parent with parentRef.")]
    public Task<string> CreateTickets(
        [Description("Space key.")] string space,
        [Description("The tickets to create, in order.")] List<TicketDraft> items) => runner.WriteAsync(async () =>
    {
        if (items.Count is 0 or > MaxBatch)
        {
            throw new McpException($"Send between 1 and {MaxBatch} tickets.");
        }

        var project = await resolver.SpaceAsync(space);
        var keys = new string?[items.Count];
        var results = new List<object>();
        for (var index = 0; index < items.Count; index++)
        {
            var draft = items[index];
            string? parentKey = null;
            if (draft.ParentRef is { } reference)
            {
                parentKey = reference >= 0 && reference < index ? keys[reference] : null;
                if (parentKey is null)
                {
                    results.Add(new { Index = index, Error = $"parentRef {reference} does not point to an earlier item that was created." });
                    continue;
                }
            }

            var (ticket, error) = await runner.TryAsync(() => CreateOneAsync(project, draft, parentKey));
            keys[index] = ticket?.Key;
            results.Add(ticket is null ? new { Index = index, Error = error } : new { Index = index, ticket.Key, ticket.Title });
        }

        return new { Created = keys.Count(key => key is not null), Failed = keys.Count(key => key is null), Results = results };
    });

    [McpServerTool(Name = "update_ticket", Destructive = false), Description(
        "Changes any fields of a ticket; leave out what should stay. Use clear to empty a field. " +
        "Status changes only through transition_ticket. The reporter is set when the ticket is created and never changes.")]
    public Task<string> UpdateTicket(
        [Description("Ticket key.")] string key,
        string? title = null,
        [Description("The whole new description in Markdown. To change part of it, use edit_description.")] string? description = null,
        [Description("Ticket type name.")] string? type = null,
        [Description("Priority name.")] string? priority = null,
        [Description("Assignee: me, an email, a name or a user id.")] string? assignee = null,
        [Description("Parent ticket key.")] string? parent = null,
        [Description("Replaces all labels.")] List<string>? labels = null,
        [Description("Labels to add, keeping the others.")] List<string>? addLabels = null,
        [Description("Labels to remove.")] List<string>? removeLabels = null,
        decimal? storyPoints = null,
        [Description("Date like 2026-11-30.")] string? startDate = null,
        [Description("Date like 2026-11-30.")] string? dueDate = null,
        [Description("Sprint name, active, or backlog.")] string? sprint = null,
        [Description("Fields to empty: description, assignee, parent, labels, storyPoints, startDate, dueDate.")] List<string>? clear = null,
        [Description("The concurrencyStamp from get_ticket, to refuse the change if someone edited the ticket since.")] string? concurrencyStamp = null)
        => runner.WriteAsync(async () =>
    {
        var ticket = await resolver.TicketAsync(key);
        var updated = await ApplyAsync(ticket, new Changes
        {
            Title = title, Description = description, Type = type, Priority = priority, Assignee = assignee,
            Parent = parent, Labels = labels, AddLabels = addLabels, RemoveLabels = removeLabels, StoryPoints = storyPoints,
            StartDate = startDate, DueDate = dueDate, Sprint = sprint, Clear = clear,
        }, concurrencyStamp);
        return Updated(updated);
    });

    [McpServerTool(Name = "edit_description", Destructive = false), Description(
        "Changes part of a ticket description without resending it. Edits apply in order to the Markdown get_ticket returns: " +
        "replace (find must match exactly once unless all), append, prepend, replace_section (new body under a heading), " +
        "insert_after_section. Returns the new description.")]
    public Task<string> EditDescription(
        [Description("Ticket key.")] string key,
        [Description("The edits, applied in order.")] List<DescriptionEdit> edits,
        [Description("The concurrencyStamp from get_ticket, to refuse the change if someone edited the ticket since.")] string? concurrencyStamp = null)
        => runner.WriteAsync(async () =>
    {
        var ticket = await resolver.TicketAsync(key);
        var markdown = MarkdownEdits.Apply(RichTextMarkdown.ToMarkdown(ticket.Description), edits);
        var updated = await ApplyAsync(ticket, new Changes { Description = markdown, ClearDescriptionWhenEmpty = true }, concurrencyStamp);
        return new { updated.Key, Description = RichTextMarkdown.ToMarkdown(updated.Description), updated.ConcurrencyStamp };
    });

    [McpServerTool(Name = "update_tickets", Destructive = false), Description(
        "Applies the same change to up to 50 tickets, such as assigning them or adding a label. Each ticket succeeds or fails on its own.")]
    public Task<string> UpdateTickets(
        [Description("Ticket keys.")] List<string> keys,
        string? type = null,
        string? priority = null,
        [Description("Assignee: me, an email, a name or a user id.")] string? assignee = null,
        [Description("Parent ticket key.")] string? parent = null,
        [Description("Labels to add.")] List<string>? addLabels = null,
        [Description("Labels to remove.")] List<string>? removeLabels = null,
        decimal? storyPoints = null,
        string? startDate = null,
        string? dueDate = null,
        [Description("Sprint name, active, or backlog.")] string? sprint = null,
        [Description("Fields to empty: assignee, parent, labels, storyPoints, startDate, dueDate.")] List<string>? clear = null)
        => runner.WriteAsync(async () =>
    {
        if (keys.Count is 0 or > MaxBulkUpdate)
        {
            throw new McpException($"Send between 1 and {MaxBulkUpdate} keys.");
        }

        var changes = new Changes
        {
            Type = type, Priority = priority, Assignee = assignee, Parent = parent, AddLabels = addLabels,
            RemoveLabels = removeLabels, StoryPoints = storyPoints, StartDate = startDate, DueDate = dueDate, Sprint = sprint, Clear = clear,
        };
        var results = new List<BatchResult>();
        foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var (ticket, error) = await runner.TryAsync(async () => await ApplyAsync(await resolver.TicketAsync(key), changes, null));
            results.Add(new BatchResult(ticket?.Key ?? key, error));
        }

        return new { Updated = results.Count(row => row.Error is null), Failed = results.Count(row => row.Error is not null), Results = results };
    });

    private sealed record BatchResult(string Key, string? Error);

    [McpServerTool(Name = "list_transitions", ReadOnly = true), Description(
        "Lists the statuses a ticket can move to now, and what each move needs: a comment, or fields to fill in transition_ticket.")]
    public Task<string> ListTransitions([Description("Ticket key.")] string key) => runner.ReadAsync(async () =>
    {
        var ticket = await resolver.TicketAsync(key);
        return new
        {
            ticket.Key,
            Status = ticket.Status.Name,
            Transitions = (await transitions.GetAvailableAsync(ticket.Id)).Select(item => new
            {
                item.Name,
                To = item.ToStatus.Name,
                Category = item.ToStatus.Category.ToString(),
                RequiresComment = item.RequiresComment ? true : (bool?)null,
                MissingFields = item.MissingFields.Length == 0 ? null : item.MissingFields,
                Blocked = item.Problems.Length == 0 ? null : item.Problems,
            }),
        };
    });

    [McpServerTool(Name = "transition_ticket", Destructive = false), Description(
        "Moves a ticket to another status along its workflow. Fill the fields list_transitions reports as missing.")]
    public Task<string> TransitionTicket(
        [Description("Ticket key.")] string key,
        [Description("Target status name or transition name, from list_transitions.")] string status,
        [Description("Comment in Markdown, required by some moves.")] string? comment = null,
        [Description("Field values the move needs, by field key, written as in update_ticket: {\"assignee\": \"me\", \"dueDate\": \"2026-11-30\"}.")]
        Dictionary<string, JsonElement>? fields = null) => runner.WriteAsync(async () =>
    {
        var ticket = await resolver.TicketAsync(key);
        var available = await transitions.GetAvailableAsync(ticket.Id);
        var option = available.FirstOrDefault(item => string.Equals(item.ToStatus.Name, status.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault(item => string.Equals(item.Name, status.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"{ticket.Key} cannot move to {status} now. It can move to: {(available.Count == 0 ? "nothing" : string.Join(", ", available.Select(item => item.ToStatus.Name).Distinct()))}.");

        var values = new Dictionary<string, JsonElement>();
        foreach (var (field, value) in fields ?? [])
        {
            var canonical = FieldKey(field);
            values[canonical] = await ResolveFieldAsync(ticket.ProjectId, canonical, value);
        }

        await transitions.ExecuteAsync(ticket.Id, new ExecuteTransitionInput
        {
            TransitionId = option.Id,
            ConcurrencyStamp = ticket.ConcurrencyStamp,
            Comment = comment is null ? null : await HtmlAsync(comment),
            Fields = values,
        });
        return new { ticket.Key, From = ticket.Status.Name, Status = option.ToStatus.Name };
    });

    private async Task<TicketDto> CreateOneAsync(ProjectDto project, TicketDraft draft, string? parentKey)
    {
        var type = await resolver.TypeAsync(project.Id, draft.Type ?? "Task");
        parentKey ??= draft.Parent;
        var ticket = await tickets.CreateAsync(new CreateTicketInput
        {
            ProjectId = project.Id,
            TypeId = type.Id,
            Title = draft.Title,
            Description = draft.Description is null ? null : await HtmlAsync(draft.Description),
            PriorityId = draft.Priority is null ? null : (await resolver.PriorityAsync(draft.Priority)).Id,
            AssigneeId = draft.Assignee is null ? null : (await resolver.UserAsync(draft.Assignee)).Id,
            ParentId = parentKey is null ? null : (await resolver.TicketAsync(parentKey)).Id,
            Labels = draft.Labels?.ToArray(),
            StoryPoints = draft.StoryPoints,
            StartDate = draft.StartDate is null ? null : McpResolver.Date(draft.StartDate, "startDate"),
            DueDate = draft.DueDate is null ? null : McpResolver.Date(draft.DueDate, "dueDate"),
        });

        if (draft.Sprint is not null && (await resolver.SprintAsync(project.Id, draft.Sprint)).Id is { } sprintId)
        {
            await sprints.MoveTicketAsync(ticket.Id, new MoveTicketInput { SprintId = sprintId });
        }

        return ticket;
    }

    private sealed class Changes
    {
        public string? Title { get; init; }

        public string? Description { get; init; }

        public bool ClearDescriptionWhenEmpty { get; init; }

        public string? Type { get; init; }

        public string? Priority { get; init; }

        public string? Assignee { get; init; }

        public string? Parent { get; init; }

        public List<string>? Labels { get; init; }

        public List<string>? AddLabels { get; init; }

        public List<string>? RemoveLabels { get; init; }

        public decimal? StoryPoints { get; init; }

        public string? StartDate { get; init; }

        public string? DueDate { get; init; }

        public string? Sprint { get; init; }

        public List<string>? Clear { get; init; }
    }

    private async Task<TicketDto> ApplyAsync(TicketDto ticket, Changes changes, string? concurrencyStamp)
    {
        if (concurrencyStamp is not null && concurrencyStamp != ticket.ConcurrencyStamp)
        {
            throw new McpException($"{ticket.Key} changed since it was read. Read it again and retry.");
        }

        var fields = new Dictionary<string, JsonElement>();
        void Set(string field, object? value) => fields[field] = JsonSerializer.SerializeToElement(value);

        if (changes.Title is not null)
        {
            Set(TicketFieldKeys.Title, changes.Title);
        }

        if (changes.Description is not null && (changes.Description.Trim().Length > 0 || changes.ClearDescriptionWhenEmpty))
        {
            Set(TicketFieldKeys.Description, await HtmlAsync(changes.Description));
        }

        if (changes.Type is not null)
        {
            Set(TicketFieldKeys.Type, (await resolver.TypeAsync(ticket.ProjectId, changes.Type)).Id);
        }

        if (changes.Priority is not null)
        {
            Set(TicketFieldKeys.Priority, (await resolver.PriorityAsync(changes.Priority)).Id);
        }

        if (changes.Assignee is not null)
        {
            Set(TicketFieldKeys.Assignee, (await resolver.UserAsync(changes.Assignee)).Id);
        }

        if (changes.Parent is not null)
        {
            Set(TicketFieldKeys.Parent, (await resolver.TicketAsync(changes.Parent)).Id);
        }

        if (changes.Labels is not null || changes.AddLabels is not null || changes.RemoveLabels is not null)
        {
            var next = ((IEnumerable<string>?)changes.Labels ?? ticket.Labels).Concat(changes.AddLabels ?? [])
                .Where(label => !(changes.RemoveLabels ?? []).Contains(label, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Set(TicketFieldKeys.Labels, next);
        }

        if (changes.StoryPoints is not null)
        {
            Set(TicketFieldKeys.StoryPoints, changes.StoryPoints);
        }

        if (changes.StartDate is not null)
        {
            Set(TicketFieldKeys.StartDate, McpResolver.Date(changes.StartDate, "startDate").ToString("O"));
        }

        if (changes.DueDate is not null)
        {
            Set(TicketFieldKeys.DueDate, McpResolver.Date(changes.DueDate, "dueDate").ToString("O"));
        }

        foreach (var name in changes.Clear ?? [])
        {
            var field = FieldKey(name);
            if (!Clearable.Contains(field))
            {
                throw new McpException($"{name} cannot be cleared. Clearable: {string.Join(", ", Clearable)}.");
            }

            fields[field] = field switch
            {
                TicketFieldKeys.Labels => JsonSerializer.SerializeToElement(Array.Empty<string>()),
                TicketFieldKeys.Description => JsonSerializer.SerializeToElement(string.Empty),
                _ => JsonSerializer.SerializeToElement<object?>(null),
            };
        }

        var updated = fields.Count == 0
            ? ticket
            : await tickets.UpdateAsync(ticket.Id, new UpdateTicketInput { ConcurrencyStamp = ticket.ConcurrencyStamp, Fields = fields });

        if (changes.Sprint is not null)
        {
            var target = await resolver.SprintAsync(ticket.ProjectId, changes.Sprint);
            await sprints.MoveTicketAsync(ticket.Id, new MoveTicketInput { SprintId = target.Id });
            updated = await resolver.TicketAsync(ticket.Key);
        }

        return updated;
    }

    /// <summary>Takes the values an agent writes (names, keys, emails, dates) and returns what the field expects.</summary>
    private async Task<JsonElement> ResolveFieldAsync(Guid projectId, string field, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return value;
        }

        string Text() => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString();

        return field switch
        {
            TicketFieldKeys.Description => JsonSerializer.SerializeToElement(await HtmlAsync(Text())),
            TicketFieldKeys.Assignee or TicketFieldKeys.Reporter => JsonSerializer.SerializeToElement((await resolver.UserAsync(Text())).Id),
            TicketFieldKeys.Priority => JsonSerializer.SerializeToElement((await resolver.PriorityAsync(Text())).Id),
            TicketFieldKeys.Type => JsonSerializer.SerializeToElement((await resolver.TypeAsync(projectId, Text())).Id),
            TicketFieldKeys.Parent => JsonSerializer.SerializeToElement((await resolver.TicketAsync(Text())).Id),
            TicketFieldKeys.StartDate or TicketFieldKeys.DueDate => JsonSerializer.SerializeToElement(McpResolver.Date(Text(), field).ToString("O")),
            TicketFieldKeys.StoryPoints when value.ValueKind == JsonValueKind.String && decimal.TryParse(Text(), System.Globalization.CultureInfo.InvariantCulture, out var points)
                => JsonSerializer.SerializeToElement(points),
            TicketFieldKeys.Labels when value.ValueKind == JsonValueKind.String
                => JsonSerializer.SerializeToElement(Text().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)),
            _ => value,
        };
    }

    private Task<string> HtmlAsync(string markdown) => RichTextMarkdown.ToHtmlAsync(markdown, resolver.MentionAsync);

    private async Task<List<Guid>> UserIdsAsync(IEnumerable<string> people)
    {
        var ids = new List<Guid>();
        foreach (var person in people)
        {
            ids.Add((await resolver.UserAsync(person)).Id);
        }

        return ids;
    }

    private static string FieldKey(string name) =>
        FieldKeys.FirstOrDefault(key => key.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) ?? name.Trim();

    private static IEnumerable<Guid> Matching<T>(List<T> items, string name, Func<T, string> nameOf, Func<T, Guid> idOf, string what)
    {
        var ids = items.Where(item => string.Equals(nameOf(item), name.Trim(), StringComparison.OrdinalIgnoreCase)).Select(idOf).Distinct().ToList();
        return ids.Count > 0
            ? ids
            : throw new McpException($"No {what} is named {name}. Choose from: {string.Join(", ", items.Select(nameOf).Distinct(StringComparer.OrdinalIgnoreCase))}.");
    }

    private static DateTime EndOfDay(string text, string field)
    {
        var time = McpResolver.Time(text, field);
        return time.TimeOfDay == TimeSpan.Zero ? time.AddDays(1).AddTicks(-1) : time;
    }

    private static object Created(TicketDto ticket, bool replayed) => new
    {
        ticket.Key,
        ticket.Title,
        Type = ticket.Type.Name,
        Status = ticket.Status.Name,
        Replayed = replayed ? true : (bool?)null,
    };

    private static object Updated(TicketDto ticket) => new
    {
        ticket.Key,
        ticket.Title,
        Status = ticket.Status.Name,
        Priority = ticket.Priority.Name,
        Assignee = McpViews.Person(ticket.Assignee),
        ticket.Labels,
        ticket.StoryPoints,
        ticket.StartDate,
        ticket.DueDate,
        Parent = ticket.Parent?.Key,
        ticket.ConcurrencyStamp,
    };
}
