using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;

namespace SprintBoot.Assistant;

/// <summary>
/// Counts a space's assistant use. A conversation counts in the period when someone asked in it then; its messages
/// are the questions and answers of the period, and its actions the changes proposed then, decided or not.
/// </summary>
public class ProjectAssistantFacts(
    IRepository<Conversation, Guid> conversations,
    IRepository<ConversationMessage, Guid> messages,
    IRepository<AssistantAction, Guid> actions,
    ISettingProvider settings) : IProjectAssistantFacts, ITransientDependency
{
    public Task<bool> IsEnabledAsync() => settings.IsTrueAsync(AssistantSettings.Enabled);

    // Not from and to: inside a query expression, from starts a clause.
    public async Task<ProjectAssistantUsage> GetUsageAsync(Guid projectId, DateTime start, DateTime end)
    {
        var conversationQuery = await conversations.GetQueryableAsync();
        var messageQuery = await messages.GetQueryableAsync();
        var rows = await (
                from message in messageQuery
                join conversation in conversationQuery on message.ConversationId equals conversation.Id
                where conversation.ProjectId == projectId
                    && message.CreatedAt >= start && message.CreatedAt < end
                    && message.Role != ConversationRole.ToolResults
                select new { message.ConversationId, message.Role, conversation.OwnerId })
            .ToListAsync();
        var questions = rows.Where(row => row.Role == ConversationRole.User).ToList();

        var actionQuery = await actions.GetQueryableAsync();
        var proposed = await actionQuery
            .Where(action => action.ProjectId == projectId && action.RequiresConfirmation && action.CreatedAt >= start && action.CreatedAt < end)
            .Select(action => new { action.Tool, action.State, action.TicketsCreated })
            .ToListAsync();

        return new ProjectAssistantUsage(
            Conversations: questions.Select(row => row.ConversationId).Distinct().Count(),
            Messages: rows.Count,
            MessagesByUser: questions.GroupBy(row => row.OwnerId).ToDictionary(group => group.Key, group => group.Count()),
            ActionsByTool: proposed.GroupBy(action => action.Tool).ToDictionary(group => group.Key, group => group.Count()),
            Confirmed: proposed.Count(action => action.State == AssistantActionState.Confirmed),
            Declined: proposed.Count(action => action.State == AssistantActionState.Declined),
            Failed: proposed.Count(action => action.State == AssistantActionState.Failed),
            TicketsCreated: proposed.Sum(action => action.TicketsCreated));
    }
}
