using System.ClientModel;
using System.Net;

namespace SprintBoot.Assistant;

/// <summary>Reads provider failures, whose types differ by SDK.</summary>
public static class ChatFailures
{
    private static readonly string[] TooLargeSigns =
    [
        "status: 413",
        "request entity too large",
        "payload too large",
        "request too large",
        "request_too_large",
        "context_length_exceeded",
        "prompt is too long",
        "maximum context length",
    ];

    /// <summary>
    /// The request was more than the provider takes: HTTP 413 from it or a proxy in front, or a context window error.
    /// </summary>
    public static bool IsTooLarge(Exception failure)
    {
        for (var current = failure; current is not null; current = current.InnerException)
        {
            if (current is ClientResultException { Status: 413 } or HttpRequestException { StatusCode: HttpStatusCode.RequestEntityTooLarge })
            {
                return true;
            }

            // The Anthropic SDK's exceptions carry the status as a property of their own.
            if (current.GetType().GetProperty("StatusCode")?.GetValue(current) is HttpStatusCode.RequestEntityTooLarge)
            {
                return true;
            }

            var message = current.Message.ToLowerInvariant();
            if (TooLargeSigns.Any(sign => message.Contains(sign, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}
