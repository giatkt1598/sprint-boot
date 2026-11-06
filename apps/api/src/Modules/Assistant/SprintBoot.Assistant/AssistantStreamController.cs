using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Uow;

namespace SprintBoot.Assistant;

/// <summary>Answers as server-sent events, one JSON <see cref="AssistantEventDto"/> per event.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/assistant/conversations/{id:guid}")]
[Authorize]
public class AssistantStreamController(AssistantChatAppService chat) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
        do
        {
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(enumerator.Current, Json)}\n\n", HttpContext.RequestAborted);
            await Response.Body.FlushAsync(HttpContext.RequestAborted);
        }
        while (await enumerator.MoveNextAsync());
    }
}
