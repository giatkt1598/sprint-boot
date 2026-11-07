using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using SprintBoot.Issues;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Settings;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace SprintBoot.Assistant;

/// <summary>Conversations belong to the person who started them; to anyone else they do not exist.</summary>
[Authorize]
public class AssistantAppService(
    IRepository<Conversation, Guid> conversations,
    IRepository<ConversationMessage, Guid> messages,
    IRepository<AssistantAction, Guid> actions,
    IRepository<AiConnection, Guid> connections,
    IIssuePlanningGateway issues,
    IProjectAccess projectAccess,
    IAssistantToolbox toolbox,
    ISettingProvider settings)
    : ApplicationService, IAssistantAppService
{
    public async Task<AssistantStatusDto> GetStatusAsync() => new()
    {
        Available = await settings.IsTrueAsync(AssistantSettings.Enabled) && await connections.AnyAsync(item => item.IsDefault),
    };

    public async Task<List<ConversationDto>> GetListAsync(Guid? issueId)
    {
        var owner = CurrentUser.GetId();
        var list = await conversations.GetListAsync(item => item.OwnerId == owner && (issueId == null || item.IssueId == issueId));
        return await ToDtosAsync(list.OrderByDescending(item => item.UpdatedAt).ToList());
    }

    public async Task<ConversationDetailDto> GetAsync(Guid id)
    {
        var conversation = await GetOwnAsync(conversations, id, CurrentUser.GetId());
        var history = await messages.GetListAsync(item => item.ConversationId == id);
        var calls = await actions.GetListAsync(item => item.ConversationId == id);
        return new ConversationDetailDto
        {
            Conversation = (await ToDtosAsync([conversation])).Single(),
            Messages = [.. await AssistantViews.ToMessagesAsync(history.OrderBy(item => item.Sequence), calls, toolbox)],
        };
    }

    public async Task<ConversationDto> CreateAsync(CreateConversationInput input)
    {
        if (input.IssueId is { } issueId)
        {
            var card = await issues.FindCardAsync(issueId) ?? throw new EntityNotFoundException(typeof(IssueCardDto), issueId);
            await projectAccess.CheckAsync(await issues.GetProjectIdAsync(card.Id), ProjectPermissions.ViewIssues);
        }

        var conversation = new Conversation(GuidGenerator.Create(), CurrentUser.GetId(), input.IssueId, string.Empty, Clock.Now);
        await conversations.InsertAsync(conversation, autoSave: true);
        return (await ToDtosAsync([conversation])).Single();
    }

    public async Task<ConversationDto> RenameAsync(Guid id, RenameConversationInput input)
    {
        var conversation = await GetOwnAsync(conversations, id, CurrentUser.GetId());
        conversation.Title = input.Title.Trim();
        conversation.TitleState = ConversationTitleState.Custom;
        await conversations.UpdateAsync(conversation, autoSave: true);
        return (await ToDtosAsync([conversation])).Single();
    }

    public async Task DeleteAsync(Guid id)
    {
        var conversation = await GetOwnAsync(conversations, id, CurrentUser.GetId());
        await actions.DeleteAsync(item => item.ConversationId == id);
        await messages.DeleteAsync(item => item.ConversationId == id);
        await conversations.DeleteAsync(conversation);
    }

    internal static async Task<Conversation> GetOwnAsync(IRepository<Conversation, Guid> conversations, Guid id, Guid owner)
    {
        var conversation = await conversations.FindAsync(id);
        return conversation is null || conversation.OwnerId != owner
            ? throw new EntityNotFoundException(typeof(Conversation), id)
            : conversation;
    }

    private async Task<List<ConversationDto>> ToDtosAsync(List<Conversation> list)
    {
        var keys = new Dictionary<Guid, string?>();
        foreach (var issueId in list.Where(item => item.IssueId != null).Select(item => item.IssueId!.Value).Distinct())
        {
            keys[issueId] = (await issues.FindCardAsync(issueId))?.Key;
        }

        return [.. list.Select(item => new ConversationDto
        {
            Id = item.Id,
            Title = item.Title,
            IssueId = item.IssueId,
            IssueKey = item.IssueId is { } issue ? keys.GetValueOrDefault(issue) : null,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
        })];
    }
}

/// <summary>How stored turns look to the person.</summary>
public static partial class AssistantViews
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<List<AssistantMessageDto>> ToMessagesAsync(
        IEnumerable<ConversationMessage> history, List<AssistantAction> calls, IAssistantToolbox toolbox)
    {
        var result = new List<AssistantMessageDto>();
        foreach (var message in history)
        {
            if (message.Role == ConversationRole.User)
            {
                result.Add(new AssistantMessageDto
                {
                    Id = message.Id, Role = "user", Text = message.Text, Tools = [], Actions = [], Sources = [], CreatedAt = message.CreatedAt,
                });
            }
            else if (message.Role == ConversationRole.Assistant)
            {
                result.Add(await ToMessageAsync(message, calls.Where(call => call.MessageId == message.Id), toolbox));
            }
        }

        return result;
    }

    public static async Task<AssistantMessageDto> ToMessageAsync(ConversationMessage message, IEnumerable<AssistantAction> calls, IAssistantToolbox toolbox)
    {
        var own = calls.ToList();
        return new AssistantMessageDto
        {
            Id = message.Id,
            Role = "assistant",
            Text = message.Text,
            Html = message.Text.Length == 0 ? null : await toolbox.RenderAsync(message.Text),
            Tools = [.. ToolCalls(message).Select(call => call.Name)],
            Actions = [.. own.Where(call => call.RequiresConfirmation).Select(ToDto)],
            Sources = Sources(message.Text),
            StopReason = message.StopReason,
            CreatedAt = message.CreatedAt,
        };
    }

    public static AssistantActionDto ToDto(AssistantAction action) => new()
    {
        Id = action.Id,
        Tool = action.Tool,
        Arguments = action.Arguments,
        State = action.State,
        Result = action.Result,
    };

    public static List<AssistantToolCall> ToolCalls(ConversationMessage message) =>
        message.ToolsJson is null ? [] : JsonSerializer.Deserialize<List<AssistantToolCall>>(message.ToolsJson, Json) ?? [];

    /// <summary>Issues the text links, in the order it first mentions them.</summary>
    public static List<string> Sources(string text) =>
        [.. IssueLink().Matches(text).Select(match => match.Groups[1].Value).Distinct()];

    [GeneratedRegex(@"/issues/([A-Z][A-Z0-9_]*-\d+)")]
    private static partial Regex IssueLink();
}

/// <summary>
/// Runs a turn of the conversation as server-sent events: the model answers, read tools run at once, and writes wait for the
/// person to confirm. It runs inside the person's own request, so every tool works with their permissions.
/// </summary>
[Authorize]
[RemoteService(IsEnabled = false)]
[UnitOfWork(IsDisabled = true)]
public class AssistantChatAppService(
    IRepository<Conversation, Guid> conversations,
    IRepository<ConversationMessage, Guid> messages,
    IRepository<AssistantAction, Guid> actions,
    IRepository<AiConnection, Guid> connections,
    IIssuePlanningGateway issues,
    IAssistantToolbox toolbox,
    ChatProviders providers,
    ISettingProvider settings,
    IStringEncryptionService encryption,
    IAbpDistributedLock locks,
    IUnitOfWorkManager unitOfWork)
    : ApplicationService
{
    /// <summary>Tool rounds one question may take before the assistant has to answer.</summary>
    private const int MaxSteps = 12;

    private const int TitleLength = 60;

    public const string SystemPrompt = """
        You are the assistant of Sprint Boot, a work management app where teams track issues in spaces.
        You help the person you are talking with find issues, understand them, summarize them, explain the terms they use,
        and, when asked, change them.

        - Use the tools to look things up before you answer anything about issues, spaces, sprints or people. Never guess
          issue content, keys, statuses or names.
        - Link every issue you mention as a Markdown link [KEY](/issues/KEY), so the person can open it and check.
        - When the tools find nothing, or the data you need is missing or out of your reach, say so plainly and say what you
          looked at. Do not fill gaps with assumptions.
        - Tools run with the person's own permissions. An issue you cannot read does not exist for you.
        - Tools that change data only run after the person confirms them in the app. Propose such a change only when the
          person asked for it, and never say it happened until the tool result confirms it.
        - Answer in the language the person writes in. Keep answers short and use Markdown lists and tables where they help.
        """;

    public async IAsyncEnumerable<AssistantEventDto> SendAsync(
        Guid conversationId, SendAssistantMessageInput input, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var owner = CurrentUser.GetId();
        await using var handle = await locks.TryAcquireAsync($"assistant:{conversationId}", cancellationToken: cancellationToken);
        if (handle is null)
        {
            yield return Error(AssistantErrors.Busy);
            yield break;
        }

        var connection = await DefaultConnectionAsync();
        Conversation conversation;
        using (var uow = unitOfWork.Begin(requiresNew: true))
        {
            conversation = await AssistantAppService.GetOwnAsync(conversations, conversationId, owner);
            if (await actions.AnyAsync(item => item.ConversationId == conversationId && item.State == AssistantActionState.Proposed))
            {
                // A new question answers the pending ones: unconfirmed changes are declined.
                await DeclinePendingAsync(conversationId);
            }

            await uow.CompleteAsync();
        }

        if (connection is null)
        {
            yield return Error(AssistantErrors.Unavailable);
            yield break;
        }

        var text = input.Text.Trim();
        var prompt = $"{await ContextAsync(conversation, input.IssueId, cancellationToken)}{text}";
        using (var uow = unitOfWork.Begin(requiresNew: true))
        {
            var question = ConversationMessage.FromUser(GuidGenerator.Create(), conversationId, text, prompt, Clock.Now);
            question.Sequence = await NextSequenceAsync(conversationId);
            await messages.InsertAsync(question);
            conversation = await conversations.GetAsync(conversationId);
            if (conversation.Title.Length == 0)
            {
                conversation.Title = text.Length <= TitleLength ? text : $"{text[..TitleLength].TrimEnd()}…";
            }

            conversation.UpdatedAt = Clock.Now;
            await conversations.UpdateAsync(conversation);
            await uow.CompleteAsync();
        }

        await foreach (var item in RunAsync(conversationId, connection, cancellationToken))
        {
            yield return item;
        }
    }

    public async IAsyncEnumerable<AssistantEventDto> DecideAsync(
        Guid conversationId, Guid actionId, bool confirm, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var owner = CurrentUser.GetId();
        await using var handle = await locks.TryAcquireAsync($"assistant:{conversationId}", cancellationToken: cancellationToken);
        if (handle is null)
        {
            yield return Error(AssistantErrors.Busy);
            yield break;
        }

        AssistantAction action;
        using (var uow = unitOfWork.Begin(requiresNew: true))
        {
            await AssistantAppService.GetOwnAsync(conversations, conversationId, owner);
            action = await actions.FindAsync(actionId) is { } found && found.ConversationId == conversationId
                ? found
                : throw new EntityNotFoundException(typeof(AssistantAction), actionId);
            await uow.CompleteAsync();
        }

        if (action.State != AssistantActionState.Proposed || !action.RequiresConfirmation)
        {
            yield return Error(AssistantErrors.ActionNotPending);
            yield break;
        }

        if (confirm)
        {
            yield return new AssistantEventDto { Type = "tool", Tool = action.Tool, State = "running" };
            var outcome = await toolbox.InvokeAsync(action.Tool, Parse(action.Arguments), cancellationToken);
            action.State = outcome.IsError ? AssistantActionState.Failed : AssistantActionState.Confirmed;
            action.Result = outcome.Content;
            yield return new AssistantEventDto { Type = "tool", Tool = action.Tool, State = outcome.IsError ? "failed" : "done" };
        }
        else
        {
            action.State = AssistantActionState.Declined;
            action.Result = "The person declined this action. Nothing was changed.";
        }

        bool settled;
        using (var uow = unitOfWork.Begin(requiresNew: true))
        {
            await actions.UpdateAsync(action);
            var siblings = await actions.GetListAsync(item => item.MessageId == action.MessageId);
            settled = siblings.All(item => item.Id == action.Id || item.State != AssistantActionState.Proposed);
            if (settled)
            {
                await AnswerToolCallsAsync(conversationId, action.MessageId, siblings.Select(item => item.Id == action.Id ? action : item).ToList());
            }

            await uow.CompleteAsync();
        }

        yield return new AssistantEventDto { Type = "action", Action = AssistantViews.ToDto(action) };
        if (!settled)
        {
            yield return new AssistantEventDto { Type = "done" };
            yield break;
        }

        var connection = await DefaultConnectionAsync();
        if (connection is null)
        {
            yield return Error(AssistantErrors.Unavailable);
            yield break;
        }

        await foreach (var item in RunAsync(conversationId, connection, cancellationToken))
        {
            yield return item;
        }
    }

    private async IAsyncEnumerable<AssistantEventDto> RunAsync(Guid conversationId, AiConnection connection, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var provider = providers.For(connection.Provider);
        var chat = AssistantAdminAppService.ToChat(connection, encryption);
        var tools = toolbox.Tools.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToList();

        for (var step = 0; step < MaxSteps; step++)
        {
            var request = new ChatRequest(SystemPrompt, await HistoryAsync(conversationId), tools);
            ChatCompleted? completed = null;
            Exception? failure = null;
            await using (var stream = provider.StreamAsync(chat, request, cancellationToken).GetAsyncEnumerator(cancellationToken))
            {
                while (true)
                {
                    ChatStreamEvent current;
                    try
                    {
                        if (!await stream.MoveNextAsync())
                        {
                            break;
                        }

                        current = stream.Current;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        failure = exception;
                        break;
                    }

                    if (current is ChatTextDelta delta)
                    {
                        yield return new AssistantEventDto { Type = "delta", Text = delta.Text };
                    }
                    else if (current is ChatCompleted done)
                    {
                        completed = done;
                    }
                }
            }

            if (failure is not null || completed is null)
            {
                Logger.LogWarning(failure, "Assistant provider {Provider} failed", connection.Provider);
                yield return new AssistantEventDto { Type = "error", Code = AssistantErrors.ProviderFailed, Text = failure?.Message };
                yield break;
            }

            var turn = completed.Turn;
            var saved = ConversationMessage.FromAssistant(
                GuidGenerator.Create(), conversationId, turn.Text,
                turn.ToolCalls.Count == 0 ? null : JsonSerializer.Serialize(turn.ToolCalls, AssistantViews.Json),
                turn.NativeJson, connection.Provider, connection.Model, completed.StopReason, completed.Usage, Clock.Now);
            var calls = new List<AssistantAction>();
            using (var uow = unitOfWork.Begin(requiresNew: true))
            {
                saved.Sequence = await NextSequenceAsync(conversationId);
                await messages.InsertAsync(saved);
                foreach (var call in turn.ToolCalls)
                {
                    var tool = tools.FirstOrDefault(item => item.Name == call.Name);
                    calls.Add(new AssistantAction(GuidGenerator.Create(), conversationId, saved.Id, call.Id, call.Name, call.Arguments, tool is not { ReadOnly: true }));
                }

                await actions.InsertManyAsync(calls);
                if (calls.Count == 0 && turn.Text.Length > 0)
                {
                    // The first answer is enough to say what the conversation is about.
                    var conversation = await conversations.GetAsync(conversationId);
                    if (conversation.TitleState == ConversationTitleState.Draft)
                    {
                        conversation.TitleState = ConversationTitleState.Pending;
                        await conversations.UpdateAsync(conversation);
                    }
                }

                await uow.CompleteAsync();
            }

            if (calls.Count == 0)
            {
                yield return new AssistantEventDto { Type = "message", Message = await AssistantViews.ToMessageAsync(saved, [], toolbox) };
                yield return new AssistantEventDto { Type = "done" };
                yield break;
            }

            // Reads run now; writes wait. All answers go back to the model together, once every write is decided.
            foreach (var call in calls.Where(call => !call.RequiresConfirmation))
            {
                yield return new AssistantEventDto { Type = "tool", Tool = call.Tool, State = "running" };
                var outcome = tools.Any(tool => tool.Name == call.Tool)
                    ? await toolbox.InvokeAsync(call.Tool, Parse(call.Arguments), cancellationToken)
                    : new AssistantToolResult($"There is no tool named {call.Tool}.", true);
                call.State = outcome.IsError ? AssistantActionState.Failed : AssistantActionState.Confirmed;
                call.Result = outcome.Content;
                yield return new AssistantEventDto { Type = "tool", Tool = call.Tool, State = outcome.IsError ? "failed" : "done" };
            }

            var waiting = calls.Where(call => call.RequiresConfirmation).ToList();
            using (var uow = unitOfWork.Begin(requiresNew: true))
            {
                await actions.UpdateManyAsync(calls.Where(call => !call.RequiresConfirmation));
                if (waiting.Count == 0)
                {
                    await AnswerToolCallsAsync(conversationId, saved.Id, calls);
                }

                await uow.CompleteAsync();
            }

            if (waiting.Count > 0)
            {
                foreach (var action in waiting)
                {
                    yield return new AssistantEventDto { Type = "action", Action = AssistantViews.ToDto(action) };
                }

                yield return new AssistantEventDto { Type = "message", Message = await AssistantViews.ToMessageAsync(saved, calls, toolbox) };
                yield return new AssistantEventDto { Type = "done" };
                yield break;
            }

            if (saved.Text.Length > 0)
            {
                yield return new AssistantEventDto { Type = "message", Message = await AssistantViews.ToMessageAsync(saved, calls, toolbox) };
            }
        }

        yield return new AssistantEventDto { Type = "error", Code = AssistantErrors.ProviderFailed, Text = "The assistant used too many steps for one question." };
    }

    private async Task<AiConnection?> DefaultConnectionAsync()
    {
        using var uow = unitOfWork.Begin(requiresNew: true);
        if (!await settings.IsTrueAsync(AssistantSettings.Enabled))
        {
            return null;
        }

        var connection = await connections.FirstOrDefaultAsync(item => item.IsDefault);
        await uow.CompleteAsync();
        return connection;
    }

    /// <summary>
    /// Today, and the issues as they are now: the one the conversation is about and the one the person has open, so the
    /// model never has to ask which ticket "this" is.
    /// </summary>
    private async Task<string> ContextAsync(Conversation conversation, Guid? viewingIssueId, CancellationToken cancellationToken)
    {
        var parts = new List<string> { $"Today is {Clock.Now:yyyy-MM-dd}." };
        if (conversation.IssueId is { } issueId)
        {
            var (key, content) = await ReadIssueAsync(issueId, cancellationToken);
            parts.Add(key is null
                ? "The issue this conversation was about no longer exists."
                : content is null
                    ? $"This conversation is about issue {key}, which the person can no longer open."
                    : $"This conversation is about issue [{key}](/issues/{key}) (id {issueId}).");
            if (content is not null && (viewingIssueId is null || viewingIssueId == issueId))
            {
                parts.Add($"When the person says \"this issue\", \"this ticket\" or \"this page\", they mean {key}.");
            }

            if (content is not null)
            {
                parts.Add($"Its current content:\n{content}");
            }
        }

        if (viewingIssueId is { } viewing && viewing != conversation.IssueId)
        {
            // An issue the person cannot read is left out, without even its key.
            var (key, content) = await ReadIssueAsync(viewing, cancellationToken);
            if (key is not null && content is not null)
            {
                parts.Add($"The person has issue [{key}](/issues/{key}) (id {viewing}) open now, so \"this issue\", \"this ticket\" or \"this page\" means {key}. Its current content:\n{content}");
            }
        }

        return $"<context>{string.Join(' ', parts)}</context>\n\n";
    }

    /// <summary>The key, and the content the get_issue tool gives the person, or null when they cannot read it.</summary>
    private async Task<(string? Key, string? Content)> ReadIssueAsync(Guid issueId, CancellationToken cancellationToken)
    {
        string? key;
        using (var uow = unitOfWork.Begin(requiresNew: true))
        {
            key = (await issues.FindCardAsync(issueId))?.Key;
            await uow.CompleteAsync();
        }

        if (key is null)
        {
            return (null, null);
        }

        var issue = await toolbox.InvokeAsync("get_issue", Parse(JsonSerializer.Serialize(new { key, includeComments = true })), cancellationToken);
        return (key, issue.IsError ? null : issue.Content);
    }

    private async Task<List<ChatTurn>> HistoryAsync(Guid conversationId)
    {
        using var uow = unitOfWork.Begin(requiresNew: true);
        var history = (await messages.GetListAsync(item => item.ConversationId == conversationId)).OrderBy(item => item.Sequence);
        var turns = new List<ChatTurn>();
        foreach (var message in history)
        {
            turns.Add(message.Role switch
            {
                ConversationRole.User => new UserTurn(message.Prompt ?? message.Text),
                ConversationRole.Assistant => new AssistantTurn(message.Text, AssistantViews.ToolCalls(message), message.NativeJson, message.Provider),
                _ => new ToolResultsTurn(JsonSerializer.Deserialize<List<AssistantToolOutcome>>(message.ToolsJson!, AssistantViews.Json) ?? []),
            });
        }

        await uow.CompleteAsync();
        return turns;
    }

    /// <summary>One message with every answer, in the order the model called the tools.</summary>
    private async Task AnswerToolCallsAsync(Guid conversationId, Guid messageId, List<AssistantAction> calls)
    {
        var message = await messages.GetAsync(messageId);
        var order = AssistantViews.ToolCalls(message).Select(call => call.Id).ToList();
        var results = calls
            .OrderBy(call => order.IndexOf(call.ToolCallId))
            .Select(call => new AssistantToolOutcome(call.ToolCallId, call.Result ?? string.Empty, call.State is not AssistantActionState.Confirmed))
            .ToList();
        var answer = ConversationMessage.FromToolResults(GuidGenerator.Create(), conversationId, JsonSerializer.Serialize(results, AssistantViews.Json), Clock.Now);
        answer.Sequence = await NextSequenceAsync(conversationId);
        await messages.InsertAsync(answer);
    }

    private async Task DeclinePendingAsync(Guid conversationId)
    {
        var pending = await actions.GetListAsync(item => item.ConversationId == conversationId && item.State == AssistantActionState.Proposed);
        foreach (var group in pending.GroupBy(item => item.MessageId))
        {
            foreach (var action in group)
            {
                action.State = AssistantActionState.Declined;
                action.Result = "The person asked something else instead. Nothing was changed.";
            }

            await actions.UpdateManyAsync(group);
            await AnswerToolCallsAsync(conversationId, group.Key, await actions.GetListAsync(item => item.MessageId == group.Key));
        }
    }

    private async Task<int> NextSequenceAsync(Guid conversationId)
    {
        var queryable = await messages.GetQueryableAsync();
        var last = await AsyncExecuter.MaxAsync(queryable.Where(item => item.ConversationId == conversationId).Select(item => (int?)item.Sequence));
        return (last ?? 0) + 1;
    }

    private static JsonElement Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }

    private static AssistantEventDto Error(string code) => new() { Type = "error", Code = code };
}
