using System.Globalization;
using System.Text;
using System.Text.Json;
using SprintBoot.Identity;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace SprintBoot.Tickets;

/// <summary>A field people define for tickets: for the whole workspace (no space) or for one space.</summary>
/// <remarks>Deleting one is a soft delete: it leaves every ticket, and the values it held stay stored.</remarks>
public class CustomField : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid? ProjectId { get; private set; }

    public string Key { get; private set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public CustomFieldType Type { get; private set; }

    /// <summary>JSON, as the ticket API takes the value.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>A new ticket needs a value, and a value cannot be emptied.</summary>
    public bool IsRequired { get; set; }

    protected CustomField()
    {
    }

    public CustomField(Guid id, Guid? projectId, string key, string name, CustomFieldType type)
        : base(id)
    {
        ProjectId = projectId;
        Key = key;
        Name = name;
        Type = type;
    }
}

/// <summary>An option of a select field. Options are hidden, never removed, once a ticket has one.</summary>
public class CustomFieldOption : Entity<Guid>
{
    public Guid FieldId { get; private set; }

    public string Label { get; set; } = null!;

    public string? Color { get; set; }

    public int Order { get; set; }

    public bool IsHidden { get; set; }

    protected CustomFieldOption()
    {
    }

    public CustomFieldOption(Guid id, Guid fieldId, string label)
        : base(id)
    {
        FieldId = fieldId;
        Label = label;
    }
}

/// <summary>A field on in a space: which ticket types show it, and where it sits among the others.</summary>
public class ProjectCustomField : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public Guid FieldId { get; private set; }

    public int Order { get; set; }

    public List<Guid> TicketTypeIds { get; set; } = [];

    protected ProjectCustomField()
    {
    }

    public ProjectCustomField(Guid id, Guid projectId, Guid fieldId)
        : base(id)
    {
        ProjectId = projectId;
        FieldId = fieldId;
    }
}

/// <summary>The order of a space's Details sidebar, built-in and custom fields together, and the built-in ones it hides.</summary>
public class ProjectFieldLayout : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public List<string> Order { get; set; } = [];

    public List<string> Hidden { get; set; } = [];

    protected ProjectFieldLayout()
    {
    }

    public ProjectFieldLayout(Guid id, Guid projectId)
        : base(id)
    {
        ProjectId = projectId;
    }
}

/// <summary>
/// A value of one ticket in one field, in the column of its kind, so filters and sorting use plain indexes. A
/// multi-select field keeps one row per chosen option.
/// </summary>
public class TicketFieldValue : Entity<Guid>
{
    public Guid TicketId { get; private set; }

    public Guid FieldId { get; private set; }

    public string? TextValue { get; set; }

    public decimal? NumberValue { get; set; }

    public DateOnly? DateValue { get; set; }

    public Guid? UserId { get; set; }

    public Guid? OptionId { get; set; }

    public bool? BoolValue { get; set; }

    protected TicketFieldValue()
    {
    }

    public TicketFieldValue(Guid id, Guid ticketId, Guid fieldId)
        : base(id)
    {
        TicketId = ticketId;
        FieldId = fieldId;
    }
}

/// <summary>Keys, colours and the checks that are the same wherever a field is written.</summary>
public static class CustomFieldRules
{
    public static readonly string[] Colors = ["gray", "blue", "green", "amber", "red", "violet"];

    public static bool HasOptions(CustomFieldType type) => type is CustomFieldType.Select or CustomFieldType.MultiSelect;

    /// <summary>"Môi trường chạy" becomes "moi_truong_chay": ASCII, so it reads the same in a URL, a filter or MCP.</summary>
    public static string KeyFrom(string name)
    {
        var plain = new StringBuilder();
        foreach (var character in name.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            plain.Append(char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        }

        var key = string.Join('_', plain.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (key.Length == 0 || !char.IsAsciiLetter(key[0]))
        {
            key = "field_" + key;
        }

        return key.Length > CustomFieldConsts.MaxKeyLength ? key[..CustomFieldConsts.MaxKeyLength].TrimEnd('_') : key;
    }

    /// <summary>A field key as callers write it, with or without "cf.".</summary>
    public static string StripPrefix(string key) =>
        key.StartsWith(CustomFieldConsts.KeyPrefix, StringComparison.Ordinal) ? key[CustomFieldConsts.KeyPrefix.Length..] : key;

    public static BusinessException Invalid(CustomField field, string expected) =>
        new BusinessException(CustomFieldErrors.InvalidValue).WithData("field", field.Name).WithData("expected", expected);

    /// <summary>A value checked against the field's kind, ready to store; null clears the field.</summary>
    public static ParsedValue Parse(CustomField field, JsonElement value, IReadOnlyCollection<CustomFieldOption> options)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return ParsedValue.Empty;
        }

        switch (field.Type)
        {
            case CustomFieldType.Text:
            case CustomFieldType.Paragraph:
                var text = value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : throw Invalid(field, "text");
                if (field.Type == CustomFieldType.Text)
                {
                    text = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
                }

                if (text.Length > CustomFieldConsts.MaxTextLength)
                {
                    throw Invalid(field, "text");
                }

                return text.Length == 0 ? ParsedValue.Empty : new ParsedValue { Text = text };

            case CustomFieldType.Url:
                var address = value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : throw Invalid(field, "url");
                if (address.Length == 0)
                {
                    return ParsedValue.Empty;
                }

                if (address.Length > CustomFieldConsts.MaxTextLength
                    || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    throw Invalid(field, "url");
                }

                return new ParsedValue { Text = address };

            case CustomFieldType.Number:
                if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && Math.Abs(number) < 100_000_000_000_000m)
                {
                    return new ParsedValue { Number = decimal.Round(number, 4) };
                }

                throw Invalid(field, "number");

            case CustomFieldType.Date:
                return value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var date)
                    ? new ParsedValue { Date = date }
                    : throw Invalid(field, "date");

            case CustomFieldType.Checkbox:
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? new ParsedValue { Checked = value.GetBoolean() }
                    : throw Invalid(field, "true or false");

            case CustomFieldType.User:
                return value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var userId)
                    ? new ParsedValue { UserId = userId }
                    : throw Invalid(field, "user id");

            case CustomFieldType.Select:
                return new ParsedValue { OptionIds = [OptionOf(field, value, options)] };

            case CustomFieldType.Tags:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    throw Invalid(field, "list of words");
                }

                var tags = value.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : throw Invalid(field, "list of words"))
                    .Where(tag => tag.Length > 0)
                    .DistinctBy(tag => tag.ToLowerInvariant())
                    .ToList();
                if (tags.Count > CustomFieldConsts.MaxTags || tags.Any(tag => tag.Length > CustomFieldConsts.MaxTagLength))
                {
                    throw Invalid(field, "list of words");
                }

                return tags.Count == 0 ? ParsedValue.Empty : new ParsedValue { Tags = [.. tags.Order(StringComparer.CurrentCultureIgnoreCase)] };

            case CustomFieldType.MultiSelect:
                if (value.ValueKind != JsonValueKind.Array)
                {
                    throw Invalid(field, "list of options");
                }

                var chosen = value.EnumerateArray().Select(item => OptionOf(field, item, options)).Distinct().ToList();
                return chosen.Count == 0 ? ParsedValue.Empty : new ParsedValue { OptionIds = chosen };

            default:
                throw Invalid(field, "value");
        }
    }

    /// <summary>An option by id, or by its label as people and AI agents write it.</summary>
    private static Guid OptionOf(CustomField field, JsonElement value, IReadOnlyCollection<CustomFieldOption> options)
    {
        var option = value.ValueKind != JsonValueKind.String
            ? null
            : value.TryGetGuid(out var id)
                ? options.FirstOrDefault(item => item.Id == id)
                : options.FirstOrDefault(item => string.Equals(item.Label, value.GetString()!.Trim(), StringComparison.CurrentCultureIgnoreCase));
        return option?.Id ?? throw new BusinessException(CustomFieldErrors.OptionInvalid).WithData("field", field.Name);
    }
}

/// <summary>A checked value: the member of its kind is set, or none for an empty field.</summary>
public sealed class ParsedValue
{
    public static readonly ParsedValue Empty = new();

    public string? Text { get; init; }

    public decimal? Number { get; init; }

    public DateOnly? Date { get; init; }

    public bool? Checked { get; init; }

    public Guid? UserId { get; init; }

    public List<Guid> OptionIds { get; init; } = [];

    public List<string> Tags { get; init; } = [];

    public bool IsEmpty => Text is null && Number is null && Date is null && Checked is null && UserId is null && OptionIds.Count == 0 && Tags.Count == 0;
}
