using System.Text.Json;
using System.Text.RegularExpressions;

namespace SprintBoot.Assistant;

/// <summary>Reads what an assistant action is about from the tool's arguments and answer.</summary>
public static partial class ActionSpace
{
    [GeneratedRegex("^([A-Za-z][A-Za-z0-9_]*)-[0-9]+$")]
    private static partial Regex TicketKey();

    [GeneratedRegex(@"""created""\s*:\s*([0-9]+)")]
    private static partial Regex CreatedCount();

    /// <summary>
    /// The space key an action names: its space argument, or the space part of the first ticket key in key, keys
    /// or items. Null for tools that name neither, such as a comment edit by id.
    /// </summary>
    public static string? KeyOf(string arguments)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(arguments).RootElement;
        }
        catch (JsonException)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (Text(root, "space") is { Length: > 0 } space)
        {
            return space.Trim().ToUpperInvariant();
        }

        var keys = new List<string?> { Text(root, "key") };
        if (root.TryGetProperty("keys", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            keys.AddRange(list.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null));
        }

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            keys.AddRange(items.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Object ? Text(item, "key") : null));
        }

        return keys
            .Select(key => key is null ? null : TicketKey().Match(key.Trim()))
            .FirstOrDefault(match => match is { Success: true })?.Groups[1].Value.ToUpperInvariant();
    }

    /// <summary>How many tickets a successful create made: one, or the count create_tickets reports.</summary>
    public static int TicketsCreated(string tool, string result) => tool switch
    {
        "create_ticket" => 1,
        "create_tickets" when CreatedCount().Match(result) is { Success: true } match => int.Parse(match.Groups[1].Value),
        _ => 0,
    };

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
