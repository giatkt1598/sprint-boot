using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace SprintBoot.Assistant;

/// <summary>
/// Names conversations once the assistant has answered their first question: the default connection reads the exchange
/// and gives it a short title, in place of the start of the question. A title the person set is never replaced.
/// </summary>
public class ConversationTitleWorker : AsyncPeriodicBackgroundWorkerBase
{
    public const string SystemPrompt = """
        You name conversations between a person and the assistant of a work management app.
        Reply with a title of at most six words that says what the conversation is about, in the language the person writes in.
        Reply with the title only: no quotes, no trailing period, no Markdown.
        """;

    /// <summary>Of each message, enough to tell the topic.</summary>
    private const int ExcerptLength = 1000;

    private const int BatchSize = 20;

    public ConversationTitleWorker(AbpAsyncTimer timer, IServiceScopeFactory scopeFactory, IConfiguration configuration)
        : base(timer, scopeFactory)
    {
        timer.Period = configuration.GetValue("Assistant:Titles:PollMilliseconds", 5000);
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        await using var handle = await services.GetRequiredService<IAbpDistributedLock>().TryAcquireAsync(nameof(ConversationTitleWorker));
        if (handle is null)
        {
            return;
        }

        var conversations = services.GetRequiredService<IRepository<Conversation, Guid>>();
        var messages = services.GetRequiredService<IRepository<ConversationMessage, Guid>>();
        var unitOfWork = services.GetRequiredService<IUnitOfWorkManager>();
        var logger = services.GetRequiredService<ILogger<ConversationTitleWorker>>();

        List<Guid> due;
        ChatConnection? connection;
        using (var uow = unitOfWork.Begin(requiresNew: true))
        {
            due = [.. (await conversations.GetListAsync(item => item.TitleState == ConversationTitleState.Pending)).Take(BatchSize).Select(item => item.Id)];
            connection = due.Count == 0 ? null : await ConnectionAsync(services);
            await uow.CompleteAsync();
        }

        if (connection is null)
        {
            // Off, or no connection: the drafts stay until the assistant can name them.
            return;
        }

        var provider = services.GetRequiredService<ChatProviders>().For(connection.Provider);
        foreach (var id in due)
        {
            string? title = null;
            try
            {
                string transcript;
                using (var uow = unitOfWork.Begin(requiresNew: true))
                {
                    transcript = Transcript(await messages.GetListAsync(item => item.ConversationId == id));
                    await uow.CompleteAsync();
                }

                var reply = new StringBuilder();
                var request = new ChatRequest(SystemPrompt, [new UserTurn(transcript)], []);
                await foreach (var item in provider.StreamAsync(connection, request, CancellationToken.None))
                {
                    if (item is ChatTextDelta delta)
                    {
                        reply.Append(delta.Text);
                    }
                }

                title = Clean(reply.ToString());
            }
            catch (Exception failure)
            {
                // The draft title stays; trying again on every poll would only repeat the failure.
                logger.LogWarning(failure, "Naming conversation {ConversationId} failed", id);
            }

            using (var uow = unitOfWork.Begin(requiresNew: true))
            {
                // The person may have renamed it meanwhile.
                var conversation = await conversations.FindAsync(id);
                if (conversation is { TitleState: ConversationTitleState.Pending })
                {
                    if (!string.IsNullOrEmpty(title))
                    {
                        conversation.Title = title;
                    }

                    conversation.TitleState = ConversationTitleState.Summarized;
                    await conversations.UpdateAsync(conversation);
                }

                await uow.CompleteAsync();
            }
        }
    }

    private static async Task<ChatConnection?> ConnectionAsync(IServiceProvider services)
    {
        if (!await services.GetRequiredService<ISettingProvider>().IsTrueAsync(AssistantSettings.Enabled))
        {
            return null;
        }

        var connection = await services.GetRequiredService<IRepository<AiConnection, Guid>>().FirstOrDefaultAsync(item => item.IsDefault);
        return connection is null ? null : AssistantAdminAppService.ToChat(connection, services.GetRequiredService<IStringEncryptionService>());
    }

    /// <summary>What the person asked and what the assistant answered, without tool traffic or the context sent along.</summary>
    internal static string Transcript(IEnumerable<ConversationMessage> history)
    {
        var text = new StringBuilder("Name this conversation.\n\n");
        foreach (var message in history.OrderBy(item => item.Sequence).Where(item => item.Role != ConversationRole.ToolResults && item.Text.Length > 0).Take(4))
        {
            var excerpt = message.Text.Length <= ExcerptLength ? message.Text : message.Text[..ExcerptLength];
            text.Append(message.Role == ConversationRole.User ? "Person: " : "Assistant: ").Append(excerpt).Append("\n\n");
        }

        return text.ToString();
    }

    /// <summary>The first line, without the quotes or period models add anyway, and short enough to store.</summary>
    internal static string Clean(string reply)
    {
        var line = reply.Trim().Split('\n', 2)[0].Trim().Trim('"', '\'', '`', '*', '#', '\u201C', '\u201D').TrimEnd('.').Trim();
        return line.Length <= AssistantConsts.MaxTitleLength ? line : line[..AssistantConsts.MaxTitleLength].TrimEnd();
    }
}
