using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Uow;

namespace SprintBoot.Assistant;

/// <summary>Answers as server-sent events, one JSON <see cref="AssistantEventDto"/> per event.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/assistant/conversations/{id:guid}")]
[Authorize]
public class AssistantStreamController(AssistantChatAppService chat, IConfiguration configuration) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// How long the stream may stay silent: while the model thinks or a tool runs, a comment line goes out instead, so
    /// proxies and CDNs (Cloudflare, nginx) do not take the quiet stream for a dead one and drop it.
    /// </summary>
    private TimeSpan Heartbeat => TimeSpan.FromSeconds(configuration.GetValue("Assistant:HeartbeatSeconds", 15d));

    [HttpPost("messages")]
    [UnitOfWork(IsDisabled = true)]
    public Task SendAsync(Guid id, [FromBody] SendAssistantMessageInput input) =>
        StreamAsync(chat.SendAsync(id, input, HttpContext.RequestAborted));

    [HttpPost("actions/{actionId:guid}/confirm")]
    [UnitOfWork(IsDisabled = true)]
    public Task ConfirmAsync(Guid id, Guid actionId) => StreamAsync(chat.DecideAsync(id, actionId, confirm: true, HttpContext.RequestAborted));

    [HttpPost("actions/{actionId:guid}/decline")]
    [UnitOfWork(IsDisabled = true)]
    public Task DeclineAsync(Guid id, Guid actionId) => StreamAsync(chat.DecideAsync(id, actionId, confirm: false, HttpContext.RequestAborted));

    private async Task StreamAsync(IAsyncEnumerable<AssistantEventDto> events)
    {
        await using var enumerator = events.GetAsyncEnumerator(HttpContext.RequestAborted);
        // The first step runs before anything is written, so not found and forbidden still answer as usual JSON errors.
        if (!await enumerator.MoveNextAsync())
        {
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        // Proxies such as nginx would otherwise hold the answer until it is complete.
        Response.Headers["X-Accel-Buffering"] = "no";
        var aborted = HttpContext.RequestAborted;
        await WriteAsync($"data: {JsonSerializer.Serialize(enumerator.Current, Json)}\n\n", aborted);
        var next = enumerator.MoveNextAsync().AsTask();
        while (true)
        {
            if (await Task.WhenAny(next, Task.Delay(Heartbeat, aborted)) != next)
            {
                await WriteAsync(": keep-alive\n\n", aborted);
                continue;
            }

            if (!await next)
            {
                break;
            }

            await WriteAsync($"data: {JsonSerializer.Serialize(enumerator.Current, Json)}\n\n", aborted);
            next = enumerator.MoveNextAsync().AsTask();
        }
    }

    private async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        await Response.WriteAsync(text, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
