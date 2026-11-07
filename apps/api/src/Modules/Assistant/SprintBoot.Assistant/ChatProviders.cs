using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using OpenAI;
using OpenAI.Chat;
using Volo.Abp.DependencyInjection;

namespace SprintBoot.Assistant;

public record AssistantToolCall(string Id, string Name, string Arguments);

public record AssistantToolOutcome(string ToolCallId, string Content, bool IsError);

public record ChatUsage(int InputTokens, int OutputTokens, int CacheReadTokens)
{
    public static readonly ChatUsage None = new(0, 0, 0);
}

/// <summary>The history in a provider-neutral form; each provider turns it into its own request.</summary>
public abstract record ChatTurn;

public record UserTurn(string Prompt) : ChatTurn;

/// <summary><paramref name="NativeJson"/> holds the provider's own blocks, replayed unchanged to that same provider.</summary>
public record AssistantTurn(string Text, IReadOnlyList<AssistantToolCall> ToolCalls, string? NativeJson, AiProvider? Provider) : ChatTurn;

public record ToolResultsTurn(IReadOnlyList<AssistantToolOutcome> Results) : ChatTurn;

public record ChatRequest(string System, IReadOnlyList<ChatTurn> Turns, IReadOnlyList<AssistantTool> Tools);

public abstract record ChatStreamEvent;

public record ChatTextDelta(string Text) : ChatStreamEvent;

public record ChatCompleted(AssistantTurn Turn, string? StopReason, ChatUsage Usage) : ChatStreamEvent;

/// <summary>What the assistant needs of a connection; the key is already decrypted.</summary>
public record ChatConnection(AiProvider Provider, string? BaseUrl, string? ApiKey, string Model, int? MaxOutputTokens, string? Effort);

public interface IChatProvider
{
    AiProvider Provider { get; }

    IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatConnection connection, ChatRequest request, CancellationToken cancellationToken);

    Task<List<AiModelDto>> ListModelsAsync(ChatConnection connection, CancellationToken cancellationToken);
}

/// <summary>Picks the provider of a connection. HTTP goes through the named client, which tests replace.</summary>
public class ChatProviders(IEnumerable<IChatProvider> providers) : ITransientDependency
{
    public const string HttpClientName = "assistant";

    public IChatProvider For(AiProvider provider) => providers.Single(item => item.Provider == provider);
}

/// <summary>
/// Claude through the official SDK. Blocks are kept as Claude sent them, signed thinking included, and the history goes
/// back append-only, so the cached prefix holds and thinking stays valid. A policy decline falls back to the model's
/// default substitute on the server.
/// </summary>
public class AnthropicChatProvider(IHttpClientFactory http) : IChatProvider, ITransientDependency
{
    public const string DefaultModel = "claude-opus-5-5";
    public const string DefaultEffort = "low";
    private const int DefaultMaxTokens = 16000;
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    public AiProvider Provider => AiProvider.Anthropic;

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatConnection connection, ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = Client(connection);

        var parameters = new MessageCreateParams
        {
            Model = string.IsNullOrWhiteSpace(connection.Model) ? DefaultModel : connection.Model,
            MaxTokens = connection.MaxOutputTokens ?? DefaultMaxTokens,
            System = request.System,
            // The last cacheable block is cached: tools and the system prompt never change, history only grows.
            CacheControl = new BetaCacheControlEphemeral(),
            OutputConfig = new BetaOutputConfig { Effort = connection.Effort ?? DefaultEffort },
            Tools = [.. request.Tools.Select(ToTool)],
            Messages = [.. ToMessages(request.Turns)],
            Betas = [FallbackBeta],
            Fallbacks = new Default(),
        };

        var blocks = new List<JsonObject>();
        var inputs = new Dictionary<int, StringBuilder>();
        var text = new StringBuilder();
        string? stopReason = null;
        int input = 0, output = 0, cacheRead = 0;

        await foreach (var streamEvent in client.Beta.Messages.CreateStreaming(parameters, cancellationToken))
        {
            if (streamEvent.TryPickStart(out var start))
            {
                input = (int)(start.Message.Usage.InputTokens + (start.Message.Usage.CacheCreationInputTokens ?? 0));
                cacheRead = (int)(start.Message.Usage.CacheReadInputTokens ?? 0);
            }
            else if (streamEvent.TryPickContentBlockStart(out var blockStart))
            {
                var index = (int)blockStart.Index;
                while (blocks.Count <= index)
                {
                    blocks.Add([]);
                }

                // The opening JSON already holds what each block type needs; deltas fill in the rest.
                var opened = JsonNode.Parse(JsonSerializer.Serialize(blockStart.ContentBlock))!.AsObject();
                if (opened["type"]?.GetValue<string>() == "tool_use")
                {
                    opened["input"] = new JsonObject();
                    inputs[index] = new StringBuilder();
                }

                blocks[index] = opened;
            }
            else if (streamEvent.TryPickContentBlockDelta(out var blockDelta))
            {
                var index = (int)blockDelta.Index;
                var block = blocks[index];
                if (blockDelta.Delta.TryPickText(out var textDelta))
                {
                    block["text"] = (block["text"]?.GetValue<string>() ?? string.Empty) + textDelta.Text;
                    text.Append(textDelta.Text);
                    yield return new ChatTextDelta(textDelta.Text);
                }
                else if (blockDelta.Delta.TryPickThinking(out var thinkingDelta))
                {
                    block["thinking"] = (block["thinking"]?.GetValue<string>() ?? string.Empty) + thinkingDelta.Thinking;
                }
                else if (blockDelta.Delta.TryPickSignature(out var signatureDelta))
                {
                    block["signature"] = signatureDelta.Signature;
                }
                else if (blockDelta.Delta.TryPickInputJson(out var inputDelta))
                {
                    inputs[index].Append(inputDelta.PartialJson);
                }
            }
            else if (streamEvent.TryPickContentBlockStop(out var blockStop))
            {
                var index = (int)blockStop.Index;
                if (inputs.TryGetValue(index, out var json))
                {
                    blocks[index]["input"] = json.Length == 0 ? new JsonObject() : JsonNode.Parse(json.ToString());
                }
            }
            else if (streamEvent.TryPickDelta(out var messageDelta))
            {
                stopReason = messageDelta.Delta.StopReason?.ToString();
                output = (int)messageDelta.Usage.OutputTokens;
            }
        }

        var calls = blocks
            .Where(block => block["type"]?.GetValue<string>() == "tool_use")
            .Select(block => new AssistantToolCall(
                block["id"]!.GetValue<string>(), block["name"]!.GetValue<string>(), block["input"]?.ToJsonString() ?? "{}"))
            .ToList();
        var native = new JsonArray([.. blocks.Select(block => (JsonNode)block)]).ToJsonString();
        yield return new ChatCompleted(
            new AssistantTurn(text.ToString(), calls, native, AiProvider.Anthropic),
            Normalize(stopReason),
            new ChatUsage(input, output, cacheRead));
    }

    public async Task<List<AiModelDto>> ListModelsAsync(ChatConnection connection, CancellationToken cancellationToken)
    {
        var models = new List<AiModelDto>();
        var page = await Client(connection).Models.List(new Anthropic.Models.Models.ModelListParams(), cancellationToken);
        while (true)
        {
            models.AddRange(page.Items.Select(model => new AiModelDto { Id = model.ID, Name = model.DisplayName }));
            if (!page.HasNext())
            {
                return models;
            }

            page = await page.Next(cancellationToken);
        }
    }

    private AnthropicClient Client(ChatConnection connection) => string.IsNullOrWhiteSpace(connection.BaseUrl)
        ? new AnthropicClient { ApiKey = connection.ApiKey ?? string.Empty, HttpClient = http.CreateClient(ChatProviders.HttpClientName) }
        : new AnthropicClient
        {
            ApiKey = connection.ApiKey ?? string.Empty,
            HttpClient = http.CreateClient(ChatProviders.HttpClientName),
            BaseUrl = connection.BaseUrl.TrimEnd('/'),
        };

    private static string? Normalize(string? stopReason) => stopReason switch
    {
        null or "end_turn" or "tool_use" or "EndTurn" or "ToolUse" => null,
        _ => stopReason,
    };

    private static BetaToolUnion ToTool(AssistantTool tool)
    {
        var schema = tool.InputSchema;
        var properties = schema.TryGetProperty("properties", out var props)
            ? props.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone())
            : [];
        var required = schema.TryGetProperty("required", out var names)
            ? names.EnumerateArray().Select(name => name.GetString()!).ToList()
            : [];
        return new BetaTool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = new() { Properties = properties, Required = required },
        };
    }

    private static IEnumerable<BetaMessageParam> ToMessages(IReadOnlyList<ChatTurn> turns)
    {
        foreach (var turn in turns)
        {
            switch (turn)
            {
                case UserTurn user:
                    yield return new BetaUserMessageParam { Content = user.Prompt };
                    break;
                case AssistantTurn assistant:
                    yield return new BetaAssistantMessageParam { Content = AssistantContent(assistant) };
                    break;
                case ToolResultsTurn results:
                    yield return new BetaUserMessageParam
                    {
                        Content = results.Results.Select(result => (BetaContentBlockParam)new BetaToolResultBlockParam
                        {
                            ToolUseID = result.ToolCallId,
                            Content = result.Content,
                            IsError = result.IsError,
                        }).ToList(),
                    };
                    break;
            }
        }
    }

    /// <summary>Claude's own blocks when Claude wrote the turn; otherwise rebuilt from text and tool calls.</summary>
    private static List<BetaContentBlockParam> AssistantContent(AssistantTurn turn)
    {
        if (turn.Provider == AiProvider.Anthropic && turn.NativeJson is { } native)
        {
            return [.. JsonNode.Parse(native)!.AsArray().Select(node => ToParam(node!.AsObject())).OfType<BetaContentBlockParam>()];
        }

        List<BetaContentBlockParam> content = [];
        if (turn.Text.Length > 0)
        {
            content.Add(new BetaTextBlockParam { Text = turn.Text });
        }

        content.AddRange(turn.ToolCalls.Select(call => (BetaContentBlockParam)new BetaToolUseBlockParam
        {
            ID = call.Id,
            Name = call.Name,
            Input = Arguments(call.Arguments),
        }));
        return content;
    }

    private static BetaContentBlockParam? ToParam(JsonObject block) => block["type"]?.GetValue<string>() switch
    {
        "text" when block["text"]?.GetValue<string>() is { Length: > 0 } value => new BetaTextBlockParam { Text = value },
        "thinking" => new BetaThinkingBlockParam
        {
            Thinking = block["thinking"]?.GetValue<string>() ?? string.Empty,
            Signature = block["signature"]?.GetValue<string>() ?? string.Empty,
        },
        "redacted_thinking" => new BetaRedactedThinkingBlockParam { Data = block["data"]!.GetValue<string>() },
        "tool_use" => new BetaToolUseBlockParam
        {
            ID = block["id"]!.GetValue<string>(),
            Name = block["name"]!.GetValue<string>(),
            Input = Arguments(block["input"]?.ToJsonString() ?? "{}"),
        },
        // Fallback markers and anything newer go back as they came.
        "text" => null,
        _ => JsonSerializer.Deserialize<BetaContentBlockParam>(block.ToJsonString()),
    };

    private static Dictionary<string, JsonElement> Arguments(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone())
            : [];
    }
}

/// <summary>Any server speaking OpenAI's chat completions: OpenAI, Ollama, LM Studio, vLLM, OpenRouter, Groq, gateways.</summary>
public class OpenAiChatProvider(IHttpClientFactory http) : IChatProvider, ITransientDependency
{
    public AiProvider Provider => AiProvider.OpenAiCompatible;

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatConnection connection, ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = new ChatClient(connection.Model, Credential(connection), Options(connection));

        var completion = new ChatCompletionOptions();
        if (connection.MaxOutputTokens is { } max)
        {
            completion.MaxOutputTokenCount = max;
        }

        foreach (var tool in request.Tools)
        {
            completion.Tools.Add(ChatTool.CreateFunctionTool(tool.Name, tool.Description, BinaryData.FromString(tool.InputSchema.GetRawText())));
        }

        var text = new StringBuilder();
        var calls = new SortedDictionary<int, (string Id, string Name, StringBuilder Arguments)>();
        string? finish = null;
        ChatUsage usage = ChatUsage.None;

        await foreach (var update in client.CompleteChatStreamingAsync(ToMessages(request), completion, cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    text.Append(part.Text);
                    yield return new ChatTextDelta(part.Text);
                }
            }

            foreach (var call in update.ToolCallUpdates)
            {
                if (!calls.TryGetValue(call.Index, out var current))
                {
                    current = (call.ToolCallId ?? $"call_{call.Index}", call.FunctionName ?? string.Empty, new StringBuilder());
                }

                if (!string.IsNullOrEmpty(call.FunctionName))
                {
                    current.Name = call.FunctionName;
                }

                if (call.FunctionArgumentsUpdate is { } arguments && !arguments.ToMemory().IsEmpty)
                {
                    current.Arguments.Append(arguments.ToString());
                }

                calls[call.Index] = current;
            }

            if (update.FinishReason is { } reason)
            {
                finish = reason switch
                {
                    ChatFinishReason.Stop or ChatFinishReason.ToolCalls => null,
                    ChatFinishReason.Length => "max_tokens",
                    ChatFinishReason.ContentFilter => "refusal",
                    _ => reason.ToString(),
                };
            }

            if (update.Usage is { } used)
            {
                usage = new ChatUsage(used.InputTokenCount, used.OutputTokenCount, used.InputTokenDetails?.CachedTokenCount ?? 0);
            }
        }

        var toolCalls = calls.Values
            .Select(call => new AssistantToolCall(call.Id, call.Name, call.Arguments.Length == 0 ? "{}" : call.Arguments.ToString()))
            .ToList();
        yield return new ChatCompleted(new AssistantTurn(text.ToString(), toolCalls, null, AiProvider.OpenAiCompatible), finish, usage);
    }

    public async Task<List<AiModelDto>> ListModelsAsync(ChatConnection connection, CancellationToken cancellationToken)
    {
        var client = new OpenAI.Models.OpenAIModelClient(Credential(connection), Options(connection));
        var models = await client.GetModelsAsync(cancellationToken);
        return [.. models.Value.Select(model => new AiModelDto { Id = model.Id })];
    }

    // Local servers such as Ollama take no key, but the client wants one.
    private static ApiKeyCredential Credential(ChatConnection connection) =>
        new(string.IsNullOrEmpty(connection.ApiKey) ? "none" : connection.ApiKey);

    private OpenAIClientOptions Options(ChatConnection connection) => new()
    {
        Endpoint = new Uri(connection.BaseUrl!),
        Transport = new HttpClientPipelineTransport(http.CreateClient(ChatProviders.HttpClientName)),
    };

    private static List<ChatMessage> ToMessages(ChatRequest request)
    {
        List<ChatMessage> messages = [new SystemChatMessage(request.System)];
        foreach (var turn in request.Turns)
        {
            switch (turn)
            {
                case UserTurn user:
                    messages.Add(new UserChatMessage(user.Prompt));
                    break;
                case AssistantTurn assistant when assistant.ToolCalls.Count > 0:
                    var message = new AssistantChatMessage(
                        assistant.ToolCalls.Select(call => ChatToolCall.CreateFunctionToolCall(call.Id, call.Name, BinaryData.FromString(call.Arguments))));
                    if (assistant.Text.Length > 0)
                    {
                        message.Content.Add(ChatMessageContentPart.CreateTextPart(assistant.Text));
                    }

                    messages.Add(message);
                    break;
                case AssistantTurn assistant:
                    messages.Add(new AssistantChatMessage(assistant.Text));
                    break;
                case ToolResultsTurn results:
                    messages.AddRange(results.Results.Select(result => new ToolChatMessage(result.ToolCallId, result.Content)));
                    break;
            }
        }

        return messages;
    }
}
