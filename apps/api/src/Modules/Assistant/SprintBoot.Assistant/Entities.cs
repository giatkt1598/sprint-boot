using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SprintBoot.Assistant;

/// <summary>A model the assistant can talk to. Exactly one is the default the assistant uses.</summary>
public class AiConnection : AuditedAggregateRoot<Guid>
{
    public string Name { get; set; } = null!;

    public AiProvider Provider { get; set; }

    public string? BaseUrl { get; set; }

    public string? EncryptedApiKey { get; set; }

    public string Model { get; set; } = null!;

    public int? MaxOutputTokens { get; set; }

    /// <summary>The largest request the provider takes, in KB; null sends whole conversations.</summary>
    public int? MaxRequestKb { get; set; }

    public string? Effort { get; set; }

    public bool IsDefault { get; set; }

    protected AiConnection()
    {
    }

    public AiConnection(Guid id)
        : base(id)
    {
    }
}

/// <summary>A person's chat with the assistant, private to them; optionally about one issue.</summary>
public class Conversation : Entity<Guid>
{
    public Guid OwnerId { get; private set; }

    public Guid? IssueId { get; private set; }

    public string Title { get; set; } = null!;

    public ConversationTitleState TitleState { get; set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; set; }

    protected Conversation()
    {
    }

    public Conversation(Guid id, Guid ownerId, Guid? issueId, string title, DateTime createdAt)
        : base(id)
    {
        OwnerId = ownerId;
        IssueId = issueId;
        Title = title;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }
}

/// <summary>Where the title comes from; only the first two may still be replaced by a summary.</summary>
public enum ConversationTitleState
{
    /// <summary>The start of the first question, until the assistant has answered it.</summary>
    Draft = 0,

    /// <summary>Answered once: <see cref="ConversationTitleWorker"/> summarizes it into a title.</summary>
    Pending = 1,

    Summarized = 2,

    /// <summary>The person renamed it; nothing replaces that.</summary>
    Custom = 3,
}

public enum ConversationRole
{
    User = 0,
    Assistant = 1,

    /// <summary>The answers of the tools the previous assistant message called.</summary>
    ToolResults = 2,
}

/// <summary>
/// One turn, never changed once written: models such as Claude expect the history they wrote back exactly as it was.
/// </summary>
public class ConversationMessage : Entity<Guid>
{
    public Guid ConversationId { get; private set; }

    public ConversationRole Role { get; private set; }

    /// <summary>Position in the conversation; several turns can share a timestamp.</summary>
    public int Sequence { get; internal set; }

    /// <summary>What the person typed, or the assistant's visible text, as Markdown.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>User turns: the text sent to the model, which also carries the issue as context.</summary>
    public string? Prompt { get; private set; }

    /// <summary>Assistant turns: tool calls as JSON. Tool result turns: the results as JSON.</summary>
    public string? ToolsJson { get; private set; }

    /// <summary>Assistant turns: the provider's own content blocks, such as Claude's signed thinking, replayed as they came.</summary>
    public string? NativeJson { get; private set; }

    public AiProvider? Provider { get; private set; }

    public string? Model { get; private set; }

    public string? StopReason { get; private set; }

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public int CacheReadTokens { get; private set; }

    public DateTime CreatedAt { get; private set; }

    protected ConversationMessage()
    {
    }

    public static ConversationMessage FromUser(Guid id, Guid conversationId, string text, string prompt, DateTime now) =>
        new() { Id = id, ConversationId = conversationId, Role = ConversationRole.User, Text = text, Prompt = prompt, CreatedAt = now };

    public static ConversationMessage FromAssistant(
        Guid id, Guid conversationId, string text, string? toolsJson, string? nativeJson, AiProvider provider, string model,
        string? stopReason, ChatUsage usage, DateTime now) =>
        new()
        {
            Id = id,
            ConversationId = conversationId,
            Role = ConversationRole.Assistant,
            Text = text,
            ToolsJson = toolsJson,
            NativeJson = nativeJson,
            Provider = provider,
            Model = model,
            StopReason = stopReason,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CacheReadTokens = usage.CacheReadTokens,
            CreatedAt = now,
        };

    public static ConversationMessage FromToolResults(Guid id, Guid conversationId, string resultsJson, DateTime now) =>
        new() { Id = id, ConversationId = conversationId, Role = ConversationRole.ToolResults, ToolsJson = resultsJson, CreatedAt = now };

    /// <summary>A copy, never saved, that sends another prompt; the stored turn stays as it was written.</summary>
    internal ConversationMessage WithPrompt(string prompt)
    {
        var copy = (ConversationMessage)MemberwiseClone();
        copy.Prompt = prompt;
        return copy;
    }
}

/// <summary>A tool call of the assistant. Writes run only once the person confirms them.</summary>
public class AssistantAction : Entity<Guid>
{
    public Guid ConversationId { get; private set; }

    /// <summary>The assistant message that asked for it.</summary>
    public Guid MessageId { get; private set; }

    public string ToolCallId { get; private set; } = null!;

    public string Tool { get; private set; } = null!;

    public string Arguments { get; private set; } = null!;

    /// <summary>False for reads, which run at once and are kept only to answer the model in one turn.</summary>
    public bool RequiresConfirmation { get; private set; }

    public AssistantActionState State { get; set; }

    public string? Result { get; set; }

    protected AssistantAction()
    {
    }

    public AssistantAction(Guid id, Guid conversationId, Guid messageId, string toolCallId, string tool, string arguments, bool requiresConfirmation)
        : base(id)
    {
        ConversationId = conversationId;
        MessageId = messageId;
        ToolCallId = toolCallId;
        Tool = tool;
        Arguments = arguments;
        RequiresConfirmation = requiresConfirmation;
        State = AssistantActionState.Proposed;
    }
}

public static class AssistantModelBuilderExtensions
{
    public const string Schema = "assistant";

    public static void ConfigureAssistant(this ModelBuilder builder)
    {
        builder.Entity<AiConnection>(b =>
        {
            b.ToTable("ai_connections", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.Name).HasMaxLength(AssistantConsts.MaxNameLength).IsRequired();
            b.Property(item => item.BaseUrl).HasMaxLength(512);
            b.Property(item => item.Model).HasMaxLength(128).IsRequired();
            b.Property(item => item.Effort).HasMaxLength(16);
        });

        builder.Entity<Conversation>(b =>
        {
            b.ToTable("conversations", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.Title).HasMaxLength(AssistantConsts.MaxTitleLength).IsRequired();
            b.HasIndex(item => new { item.OwnerId, item.UpdatedAt });
            b.HasIndex(item => item.IssueId);
            b.HasIndex(item => item.TitleState);
        });

        builder.Entity<ConversationMessage>(b =>
        {
            b.ToTable("conversation_messages", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.Text).IsRequired();
            b.Property(item => item.Model).HasMaxLength(128);
            b.Property(item => item.StopReason).HasMaxLength(64);
            b.HasIndex(item => new { item.ConversationId, item.Sequence }).IsUnique();
        });

        builder.Entity<AssistantAction>(b =>
        {
            b.ToTable("conversation_actions", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.ToolCallId).HasMaxLength(128).IsRequired();
            b.Property(item => item.Tool).HasMaxLength(128).IsRequired();
            b.HasIndex(item => item.MessageId);
        });
    }
}
