using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using SprintBoot.Tickets;
using Volo.Abp.DependencyInjection;

namespace SprintBoot.Host.Mcp;

/// <summary>
/// Custom fields as agents read and write them: by "cf." key, options by label, people as tools name them, values as
/// plain JSON.
/// </summary>
public class McpCustomFields(ICustomFieldAppService fields, McpResolver resolver) : ITransientDependency
{
    /// <summary>The fields on in a space, for get_space_fields.</summary>
    public async Task<object?> DescribeAsync(Guid projectId, IReadOnlyCollection<TicketTypeDto> types)
    {
        var list = (await fields.GetSpaceFieldsAsync(projectId)).Where(item => item.Enabled).ToList();
        return list.Count == 0 ? null : list.Select(item => new
        {
            Key = CustomFieldConsts.KeyPrefix + item.Field.Key,
            item.Field.Name,
            Kind = item.Field.Type.ToString(),
            item.Field.Description,
            ShownOn = types.Where(type => item.TicketTypeIds.Contains(type.Id)).Select(type => type.Name),
            Options = item.Field.Type is CustomFieldType.Select or CustomFieldType.MultiSelect ? item.Field.Options.Where(option => !option.IsHidden).Select(option => option.Label) : null,
        });
    }

    /// <summary>A ticket's custom fields by "cf." key; empty ones are null, so the agent sees what it can fill.</summary>
    public static Dictionary<string, object?>? View(IReadOnlyCollection<TicketCustomFieldDto>? items) =>
        items is not { Count: > 0 }
            ? null
            : items.ToDictionary(item => CustomFieldConsts.KeyPrefix + item.Field.Key, item => (object?)(item.Field.Type switch
            {
                CustomFieldType.Number => item.Number,
                CustomFieldType.Date => item.Date?.ToString("O"),
                CustomFieldType.Checkbox => item.Checked,
                CustomFieldType.User => McpViews.Person(item.User),
                CustomFieldType.Select => item.Options.FirstOrDefault()?.Label,
                CustomFieldType.MultiSelect => item.Options.Count == 0 ? null : item.Options.Select(option => option.Label).ToList(),
                _ => item.Text,
            }));

    /// <summary>
    /// Values an agent wrote, by key with or without "cf.", ready for the ticket API: people become ids; options, dates
    /// and numbers the API reads as they are.
    /// </summary>
    public async Task<Dictionary<string, JsonElement>> ResolveAsync(Guid projectId, IReadOnlyDictionary<string, JsonElement>? values)
    {
        var result = new Dictionary<string, JsonElement>();
        if (values is not { Count: > 0 })
        {
            return result;
        }

        var known = await FieldsAsync(projectId);
        foreach (var (key, value) in values)
        {
            var field = Find(known, key);
            result[CustomFieldConsts.KeyPrefix + field.Key] = field.Type == CustomFieldType.User && value.ValueKind == JsonValueKind.String && !value.TryGetGuid(out _)
                ? JsonSerializer.SerializeToElement((await resolver.UserAsync(value.GetString()!)).Id)
                : field.Type == CustomFieldType.Number && value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                    ? JsonSerializer.SerializeToElement(number)
                    : value;
        }

        return result;
    }

    /// <summary>Filters as the search takes them, with option labels and people turned into ids.</summary>
    public async Task<string[]> ResolveFiltersAsync(Guid? projectId, IEnumerable<string> filters)
    {
        var known = projectId is { } id ? await FieldsAsync(id) : (await fields.GetListAsync(null)).ToList();
        var result = new List<string>();
        foreach (var filter in filters)
        {
            var parts = filter.Split(':', 3);
            var field = Find(known, parts[0]);
            var op = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : string.Empty;
            var text = parts.Length > 2 ? parts[2] : string.Empty;
            if (op == "in")
            {
                var ids = new List<string>();
                foreach (var item in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    ids.Add(Guid.TryParse(item, out _) ? item
                        : field.Type == CustomFieldType.User ? (await resolver.UserAsync(item)).Id.ToString()
                        : field.Options.FirstOrDefault(option => string.Equals(option.Label, item, StringComparison.CurrentCultureIgnoreCase))?.Id.ToString()
                            ?? throw new McpException($"{field.Name} has no option {item}. Choose from: {string.Join(", ", field.Options.Where(option => !option.IsHidden).Select(option => option.Label))}."));
                }

                text = string.Join(',', ids);
            }

            result.Add($"{field.Key}:{op}{(op is "empty" or "any" ? string.Empty : ":" + text)}");
        }

        return [.. result];
    }

    private async Task<List<CustomFieldDto>> FieldsAsync(Guid projectId) =>
        [.. (await fields.GetSpaceFieldsAsync(projectId)).Where(item => item.Enabled).Select(item => item.Field)];

    private static CustomFieldDto Find(List<CustomFieldDto> known, string key)
    {
        var trimmed = key.Trim();
        var plain = (trimmed.StartsWith(CustomFieldConsts.KeyPrefix, StringComparison.OrdinalIgnoreCase) ? trimmed[CustomFieldConsts.KeyPrefix.Length..] : trimmed).ToLowerInvariant();
        return known.FirstOrDefault(field => field.Key == plain)
            ?? known.FirstOrDefault(field => string.Equals(field.Name, key.Trim(), StringComparison.CurrentCultureIgnoreCase))
            ?? throw new McpException(known.Count == 0
                ? $"There is no custom field {key} here: this space uses none."
                : $"There is no custom field {key}. Fields: {string.Join(", ", known.Select(field => CustomFieldConsts.KeyPrefix + field.Key))}.");
    }
}
