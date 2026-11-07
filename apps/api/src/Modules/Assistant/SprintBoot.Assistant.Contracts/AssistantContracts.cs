using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Volo.Abp.Application.Services;
using Volo.Abp.Modularity;

namespace SprintBoot.Assistant;

[DependsOn(typeof(Volo.Abp.Application.AbpDddApplicationContractsModule))]
public class AssistantContractsModule : AbpModule
{
}

/// <summary>How the assistant reaches a model.</summary>
public enum AiProvider
{
    /// <summary>The OpenAI chat completions API, as OpenAI and many local or hosted gateways serve it.</summary>
    OpenAiCompatible = 0,

    /// <summary>The Anthropic Messages API.</summary>
    Anthropic = 1,
}

public class AiConnectionDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required AiProvider Provider { get; set; }

    public string? BaseUrl { get; set; }

    public required string Model { get; set; }

    public int? MaxOutputTokens { get; set; }

    /// <summary>Anthropic only: low, medium, high, xhigh or max.</summary>
    public string? Effort { get; set; }

    /// <summary>The key itself is never returned.</summary>
    public required bool HasApiKey { get; set; }

    public required bool IsDefault { get; set; }
}

public class SaveAiConnectionInput
{
    [Required]
    [StringLength(AssistantConsts.MaxNameLength)]
    public required string Name { get; set; }

    public AiProvider Provider { get; set; }

    /// <summary>Required for OpenAI Compatible, such as https://api.openai.com/v1 or http://localhost:11434/v1.</summary>
    [StringLength(512)]
    public string? BaseUrl { get; set; }

    /// <summary>Blank keeps the stored key.</summary>
    [StringLength(1024)]
    public string? ApiKey { get; set; }

    [Required]
    [StringLength(128)]
    public required string Model { get; set; }

    [Range(256, 128000)]
    public int? MaxOutputTokens { get; set; }

    [StringLength(16)]
    public string? Effort { get; set; }

    public bool IsDefault { get; set; }
}

public class AssistantSettingsDto
{
    public required bool Enabled { get; set; }

    public required List<AiConnectionDto> Connections { get; set; }
}

public class UpdateAssistantSettingsInput
{
    public bool Enabled { get; set; }
}

public class TestAiConnectionResultDto
{
    public required bool Succeeded { get; set; }

    /// <summary>The model's reply, or what the provider said went wrong.</summary>
    public required string Message { get; set; }
}

/// <summary>The form as it is, so models load before the connection is saved.</summary>
public class ListAiModelsInput
{
    public AiProvider Provider { get; set; }

    [StringLength(512)]
    public string? BaseUrl { get; set; }

    /// <summary>Blank uses the key stored for <see cref="ConnectionId"/>.</summary>
    [StringLength(1024)]
    public string? ApiKey { get; set; }

    public Guid? ConnectionId { get; set; }
}

/// <summary>The form as it is, to test it before saving.</summary>
public class TestAiSettingsInput : ListAiModelsInput
{
    [Required]
    [StringLength(128)]
    public required string Model { get; set; }

    [StringLength(16)]
    public string? Effort { get; set; }
}

public class AiModelDto
{
    public required string Id { get; set; }

    /// <summary>A friendlier name, when the provider has one.</summary>
    public string? Name { get; set; }
}

public interface IAssistantAdminAppService : IApplicationService
{
    /// <summary>The models the provider offers with these settings.</summary>
    Task<List<AiModelDto>> ListModelsAsync(ListAiModelsInput input);

    Task<AssistantSettingsDto> GetAsync();

    Task<AssistantSettingsDto> UpdateAsync(UpdateAssistantSettingsInput input);

    Task<AiConnectionDto> CreateConnectionAsync(SaveAiConnectionInput input);

    Task<AiConnectionDto> UpdateConnectionAsync(Guid id, SaveAiConnectionInput input);

    Task DeleteConnectionAsync(Guid id);

    /// <summary>Sends one short prompt through the connection.</summary>
    Task<TestAiConnectionResultDto> TestConnectionAsync(Guid id);

    /// <summary>Sends one short prompt with unsaved settings; a blank key uses the stored one.</summary>
    Task<TestAiConnectionResultDto> TestSettingsAsync(TestAiSettingsInput input);
}

public class ConversationDto
{
    public required Guid Id { get; set; }

    public required string Title { get; set; }

    public Guid? IssueId { get; set; }

    public string? IssueKey { get; set; }

    public required DateTime CreatedAt { get; set; }

    public required DateTime UpdatedAt { get; set; }
}

public enum AssistantActionState
{
    Proposed = 0,
    Confirmed = 1,
    Declined = 2,
    Failed = 3,
}

/// <summary>A change the assistant wants to make; nothing happens until the person confirms it.</summary>
public class AssistantActionDto
{
    public required Guid Id { get; set; }

    public required string Tool { get; set; }

    /// <summary>The tool's arguments, as the model gave them.</summary>
    public required string Arguments { get; set; }

    public required AssistantActionState State { get; set; }

    /// <summary>What the tool answered once confirmed, or why it failed.</summary>
    public string? Result { get; set; }
}

public class AssistantMessageDto
{
    public required Guid Id { get; set; }

    /// <summary>user or assistant.</summary>
    public required string Role { get; set; }

    /// <summary>Markdown.</summary>
    public required string Text { get; set; }

    /// <summary>Assistant only: the text rendered to sanitized HTML.</summary>
    public string? Html { get; set; }

    /// <summary>Tools the assistant ran while writing this message.</summary>
    public required List<string> Tools { get; set; }

    public required List<AssistantActionDto> Actions { get; set; }

    /// <summary>Issue keys the message links to.</summary>
    public required List<string> Sources { get; set; }

    /// <summary>Set when the model stopped for another reason than finishing, such as a refusal or the length limit.</summary>
    public string? StopReason { get; set; }

    public required DateTime CreatedAt { get; set; }
}

public class ConversationDetailDto
{
    public required ConversationDto Conversation { get; set; }

    public required List<AssistantMessageDto> Messages { get; set; }
}

public class CreateConversationInput
{
    /// <summary>Ties the conversation to an issue, whose content then goes with every question.</summary>
    public Guid? IssueId { get; set; }
}

public class RenameConversationInput
{
    [Required]
    [StringLength(AssistantConsts.MaxTitleLength)]
    public required string Title { get; set; }
}

public class SendAssistantMessageInput
{
    [Required]
    [StringLength(AssistantConsts.MaxQuestionLength)]
    public required string Text { get; set; }
}

public class AssistantStatusDto
{
    /// <summary>On, and a default connection is set.</summary>
    public required bool Available { get; set; }
}

public interface IAssistantAppService : IApplicationService
{
    Task<AssistantStatusDto> GetStatusAsync();

    Task<List<ConversationDto>> GetListAsync(Guid? issueId);

    Task<ConversationDetailDto> GetAsync(Guid id);

    Task<ConversationDto> CreateAsync(CreateConversationInput input);

    Task<ConversationDto> RenameAsync(Guid id, RenameConversationInput input);

    Task DeleteAsync(Guid id);
}

/// <summary>One server-sent event while the assistant answers.</summary>
public class AssistantEventDto
{
    /// <summary>delta, tool, action, message, error or done.</summary>
    public required string Type { get; set; }

    public string? Text { get; set; }

    public string? Tool { get; set; }

    /// <summary>For tool events: running, done or failed.</summary>
    public string? State { get; set; }

    public AssistantActionDto? Action { get; set; }

    public AssistantMessageDto? Message { get; set; }

    public string? Code { get; set; }
}

public static class AssistantConsts
{
    public const int MaxNameLength = 64;
    public const int MaxTitleLength = 128;
    public const int MaxQuestionLength = 20000;
}

public static class AssistantErrors
{
    public const string Unavailable = "SprintBoot:AssistantUnavailable";
    public const string BaseUrlRequired = "SprintBoot:AssistantBaseUrlRequired";
    public const string InvalidEffort = "SprintBoot:AssistantInvalidEffort";
    public const string Busy = "SprintBoot:AssistantBusy";
    public const string ActionNotPending = "SprintBoot:AssistantActionNotPending";
    public const string ProviderFailed = "SprintBoot:AssistantProviderFailed";
    public const string ModelsUnavailable = "SprintBoot:AssistantModelsUnavailable";
}

/// <summary>A tool the assistant may call; the host supplies them, with the caller's own permissions.</summary>
public record AssistantTool(string Name, string Description, JsonElement InputSchema, bool ReadOnly);

public record AssistantToolResult(string Content, bool IsError);

/// <summary>Implemented by the host, which owns the MCP tools and the Markdown renderer.</summary>
public interface IAssistantToolbox
{
    IReadOnlyList<AssistantTool> Tools { get; }

    Task<AssistantToolResult> InvokeAsync(string name, JsonElement arguments, CancellationToken cancellationToken);

    /// <summary>Sanitized HTML for Markdown the assistant wrote.</summary>
    Task<string> RenderAsync(string markdown);
}
