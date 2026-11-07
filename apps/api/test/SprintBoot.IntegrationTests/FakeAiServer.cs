using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SprintBoot.IntegrationTests;

/// <summary>A scripted turn: text, then tool calls, as the model would stream them.</summary>
public record FakeAiReply(string Text, params (string Name, object Arguments)[] ToolCalls)
{
    /// <summary>Anthropic only: a signed thinking block before the text.</summary>
    public string? Thinking { get; init; }

    /// <summary>Fails with this HTTP status instead, the way a proxy in front of the provider would.</summary>
    public HttpStatusCode? Status { get; init; }

    /// <summary>How long the model takes before it starts answering.</summary>
    public TimeSpan Delay { get; init; }
}

/// <summary>
/// Streams scripted answers in the OpenAI chat completions and Anthropic Messages formats, and keeps every request body so
/// tests can check what the assistant sent.
/// </summary>
public sealed class FakeAiServer : HttpMessageHandler
{
    public const string OpenAiUrl = "http://ai.fake/v1";
    public const string AnthropicUrl = "http://anthropic.fake";

    public static readonly FakeAiServer Instance = new();

    private readonly ConcurrentQueue<FakeAiReply> _replies = new();

    public ConcurrentQueue<(string Path, JsonObject Body, HttpRequestHeaders Headers)> Requests { get; } = new();

    public void Script(params FakeAiReply[] replies)
    {
        _replies.Clear();
        Requests.Clear();
        foreach (var reply in replies)
        {
            _replies.Enqueue(reply);
        }
    }

    public List<JsonObject> Bodies => [.. Requests.Select(request => request.Body)];

    /// <summary>Questions containing this get a title from the title worker; others fail to be named and keep their draft.</summary>
    public const string NameMe = "[name me]";

    /// <summary>What the title worker asked, kept apart so it never disturbs the scripted chat.</summary>
    public ConcurrentQueue<JsonObject> TitleRequests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal))
        {
            Requests.Enqueue((request.RequestUri.AbsolutePath, new JsonObject(), request.Headers));
            var anthropic = request.RequestUri.Host == new Uri(AnthropicUrl).Host;
            object models = anthropic
                ? new
                {
                    data = new[]
                    {
                        new { type = "model", id = "claude-opus-5-5", display_name = "Claude Opus 5.5", created_at = "2026-09-01T00:00:00Z" },
                        new { type = "model", id = "claude-haiku-5-5", display_name = "Claude Haiku 5.5", created_at = "2026-09-01T00:00:00Z" },
                    },
                    has_more = false,
                    first_id = "claude-opus-5-5",
                    last_id = "claude-haiku-5-5",
                }
                : new { @object = "list", data = new[] { new { id = "llama3.3", @object = "model", created = 1, owned_by = "fake" }, new { id = "gpt-fake", @object = "model", created = 1, owned_by = "fake" } } };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(models), Encoding.UTF8, "application/json") };
        }

        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        var anthropicFormat = request.RequestUri!.AbsolutePath.EndsWith("/v1/messages", StringComparison.Ordinal);
        if (body.ToJsonString().Contains("You name conversations", StringComparison.Ordinal))
        {
            TitleRequests.Enqueue(body);
            return body.ToJsonString().Contains(NameMe.Replace("[", "\u005B", StringComparison.Ordinal), StringComparison.Ordinal)
                || body.ToJsonString().Contains(NameMe, StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    // Quoted, with a period, as models tend to answer.
                    Content = new StringContent(anthropicFormat ? Anthropic(new FakeAiReply("\"Sprint planning help.\"")) : OpenAi(new FakeAiReply("\"Sprint planning help.\"")), Encoding.UTF8, "text/event-stream"),
                }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":{\"message\":\"No title scripted\"}}") };
        }

        Requests.Enqueue((request.RequestUri!.AbsolutePath, body, request.Headers));
        if (!_replies.TryDequeue(out var reply))
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":{\"message\":\"No scripted reply\"}}") };
        }

        await Task.Delay(reply.Delay, cancellationToken);
        if (reply.Status is { } status)
        {
            return new HttpResponseMessage(status) { Content = new StringContent("<html><body><h1>413 Request Entity Too Large</h1></body></html>", Encoding.UTF8, "text/html") };
        }

        var stream = anthropicFormat ? Anthropic(reply) : OpenAi(reply);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(stream, Encoding.UTF8, "text/event-stream"),
        };
    }

    private static string Data(object value) => $"data: {JsonSerializer.Serialize(value)}\n\n";

    private static string OpenAi(FakeAiReply reply)
    {
        var chunks = new StringBuilder();
        object Chunk(object delta, string? finish, object? usage = null) => new
        {
            id = "chatcmpl-1",
            @object = "chat.completion.chunk",
            created = 1,
            model = "fake",
            choices = new[] { new { index = 0, delta, finish_reason = finish } },
            usage,
        };

        // Two parts, so the client really joins a streamed text.
        foreach (var part in Split(reply.Text))
        {
            chunks.Append(Data(Chunk(new { role = "assistant", content = part }, null)));
        }

        for (var index = 0; index < reply.ToolCalls.Length; index++)
        {
            var (name, arguments) = reply.ToolCalls[index];
            chunks.Append(Data(Chunk(new
            {
                tool_calls = new[]
                {
                    new { index, id = $"call_{index}", type = "function", function = new { name, arguments = JsonSerializer.Serialize(arguments) } },
                },
            }, null)));
        }

        chunks.Append(Data(Chunk(new { }, reply.ToolCalls.Length > 0 ? "tool_calls" : "stop", new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 })));
        chunks.Append("data: [DONE]\n\n");
        return chunks.ToString();
    }

    private static string Anthropic(FakeAiReply reply)
    {
        var events = new StringBuilder();
        void Event(string type, object data) => events.Append($"event: {type}\n").Append(Data(data));

        Event("message_start", new
        {
            type = "message_start",
            message = new
            {
                id = "msg_1",
                type = "message",
                role = "assistant",
                model = "claude-opus-5-5",
                content = Array.Empty<object>(),
                stop_reason = (string?)null,
                stop_sequence = (string?)null,
                usage = new { input_tokens = 10, output_tokens = 1, cache_read_input_tokens = 4, cache_creation_input_tokens = 0 },
            },
        });

        var index = 0;
        if (reply.Thinking is { } thinking)
        {
            Event("content_block_start", new { type = "content_block_start", index, content_block = new { type = "thinking", thinking = "", signature = "" } });
            Event("content_block_delta", new { type = "content_block_delta", index, delta = new { type = "thinking_delta", thinking } });
            Event("content_block_delta", new { type = "content_block_delta", index, delta = new { type = "signature_delta", signature = "sig-" + thinking } });
            Event("content_block_stop", new { type = "content_block_stop", index });
            index++;
        }

        if (reply.Text.Length > 0)
        {
            Event("content_block_start", new { type = "content_block_start", index, content_block = new { type = "text", text = "" } });
            foreach (var part in Split(reply.Text))
            {
                Event("content_block_delta", new { type = "content_block_delta", index, delta = new { type = "text_delta", text = part } });
            }

            Event("content_block_stop", new { type = "content_block_stop", index });
            index++;
        }

        foreach (var (name, arguments) in reply.ToolCalls)
        {
            Event("content_block_start", new { type = "content_block_start", index, content_block = new { type = "tool_use", id = $"toolu_{index}", name, input = new { } } });
            Event("content_block_delta", new { type = "content_block_delta", index, delta = new { type = "input_json_delta", partial_json = JsonSerializer.Serialize(arguments) } });
            Event("content_block_stop", new { type = "content_block_stop", index });
            index++;
        }

        Event("message_delta", new
        {
            type = "message_delta",
            delta = new { stop_reason = reply.ToolCalls.Length > 0 ? "tool_use" : "end_turn", stop_sequence = (string?)null },
            usage = new { output_tokens = 5 },
        });
        Event("message_stop", new { type = "message_stop" });
        return events.ToString();
    }

    private static IEnumerable<string> Split(string text) =>
        text.Length < 2 ? (text.Length == 0 ? [] : [text]) : [text[..(text.Length / 2)], text[(text.Length / 2)..]];
}
