using System.Globalization;
using System.Text.Json;
using SprintBoot.Identity;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace SprintBoot.Tickets;

/// <summary>A field a space shows for a ticket type, with its options.</summary>
public sealed record ApplicableField(CustomField Field, IReadOnlyList<CustomFieldOption> Options);

/// <summary>Reads and writes the custom field values of tickets; the ticket service and the workflow gateway share it.</summary>
public class CustomFieldStore(
    IRepository<CustomField, Guid> fields,
    IRepository<CustomFieldOption, Guid> options,
    IRepository<ProjectCustomField, Guid> spaceFields,
    IRepository<TicketFieldValue, Guid> values,
    IUserDirectory userDirectory,
    IGuidGenerator guids) : ITransientDependency
{
    /// <summary>The fields a space shows for a ticket type, in the space's order.</summary>
    public async Task<List<ApplicableField>> GetApplicableAsync(Guid projectId, Guid typeId) =>
        (await GetForSpaceAsync(projectId)).Where(item => item.TypeIds.Contains(typeId)).Select(item => item.Field).ToList();

    /// <summary>Every field on in the space, with the ticket types that show it.</summary>
    public async Task<List<(ApplicableField Field, List<Guid> TypeIds)>> GetForSpaceAsync(Guid projectId)
    {
        var rows = await spaceFields.GetListAsync(row => row.ProjectId == projectId);
        var ids = rows.Select(row => row.FieldId).ToList();
        var list = (await fields.GetListAsync(field => ids.Contains(field.Id))).ToDictionary(field => field.Id);
        var choices = (await options.GetListAsync(option => ids.Contains(option.FieldId))).ToLookup(option => option.FieldId);
        return [.. rows.Where(row => list.ContainsKey(row.FieldId)).OrderBy(row => row.Order).Select(row =>
            (new ApplicableField(list[row.FieldId], [.. choices[row.FieldId].OrderBy(option => option.Order)]), row.TicketTypeIds))];
    }

    public static CustomFieldDto ToDto(CustomField field, IEnumerable<CustomFieldOption> fieldOptions, int usage) => new()
    {
        Id = field.Id,
        ProjectId = field.ProjectId,
        Key = field.Key,
        Name = field.Name,
        Description = field.Description,
        Type = field.Type,
        Options = [.. fieldOptions.OrderBy(option => option.Order).Select(ToDto)],
        DefaultValue = field.DefaultValue,
        UsageCount = usage,
    };

    public static CustomFieldOptionDto ToDto(CustomFieldOption option) => new()
    {
        Id = option.Id,
        Label = option.Label,
        Color = option.Color,
        IsHidden = option.IsHidden,
    };

    /// <summary>The fields of each ticket's type, with values, in one read for all of them.</summary>
    public async Task<Dictionary<Guid, List<TicketCustomFieldDto>>> ReadAsync(IReadOnlyCollection<Ticket> tickets)
    {
        var result = tickets.ToDictionary(ticket => ticket.Id, _ => new List<TicketCustomFieldDto>());
        var bySpace = new Dictionary<Guid, List<(ApplicableField Field, List<Guid> TypeIds)>>();
        foreach (var projectId in tickets.Select(ticket => ticket.ProjectId).Distinct())
        {
            bySpace[projectId] = await GetForSpaceAsync(projectId);
        }

        if (bySpace.Values.All(list => list.Count == 0))
        {
            return result;
        }

        var ticketIds = tickets.Select(ticket => ticket.Id).ToList();
        var stored = (await values.GetListAsync(value => ticketIds.Contains(value.TicketId))).ToLookup(value => (value.TicketId, value.FieldId));
        var users = await userDirectory.GetByIdsAsync(stored.SelectMany(group => group).Where(value => value.UserId.HasValue).Select(value => value.UserId!.Value).Distinct());

        foreach (var ticket in tickets)
        {
            foreach (var (applicable, typeIds) in bySpace[ticket.ProjectId])
            {
                if (!typeIds.Contains(ticket.TypeId))
                {
                    continue;
                }

                var rows = stored[(ticket.Id, applicable.Field.Id)].ToList();
                var first = rows.FirstOrDefault();
                var chosen = rows.Where(row => row.OptionId.HasValue).Select(row => row.OptionId!.Value).ToHashSet();
                result[ticket.Id].Add(new TicketCustomFieldDto
                {
                    Field = ToDto(applicable.Field, applicable.Options, 0),
                    Text = first?.TextValue,
                    Number = first?.NumberValue,
                    Date = first?.DateValue,
                    Checked = first?.BoolValue,
                    User = first?.UserId is { } userId ? users.GetValueOrDefault(userId) : null,
                    Options = [.. applicable.Options.Where(option => chosen.Contains(option.Id)).Select(ToDto)],
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Writes one field of a ticket from a value as the API takes it. Returns what history shows before and after,
    /// or null when nothing changed.
    /// </summary>
    public async Task<(string? Old, string? New)?> SetAsync(Ticket ticket, string key, JsonElement value)
    {
        var applicable = await FindAsync(ticket, key);
        return await WriteAsync(ticket, applicable, value);
    }

    /// <summary>
    /// The values a new ticket was created with, and the defaults of the fields left out. Unknown keys and wrong values
    /// fail before anything is written.
    /// </summary>
    public async Task SetOnCreateAsync(Ticket ticket, IReadOnlyDictionary<string, JsonElement>? given)
    {
        var applicable = await GetApplicableAsync(ticket.ProjectId, ticket.TypeId);
        var byKey = (given ?? new Dictionary<string, JsonElement>()).ToDictionary(pair => CustomFieldRules.StripPrefix(pair.Key), pair => pair.Value);
        foreach (var unknown in byKey.Keys.Where(key => applicable.All(item => item.Field.Key != key)))
        {
            throw new BusinessException(TicketErrors.UnknownField).WithData("field", CustomFieldConsts.KeyPrefix + unknown);
        }

        foreach (var item in applicable)
        {
            if (byKey.TryGetValue(item.Field.Key, out var value))
            {
                await WriteAsync(ticket, item, value);
            }
            else if (item.Field.DefaultValue is { } fallback)
            {
                using var document = JsonDocument.Parse(fallback);
                await WriteAsync(ticket, item, document.RootElement.Clone());
            }
        }
    }

    /// <summary>The field the key names among those the space shows for the ticket's type.</summary>
    public async Task<ApplicableField> FindAsync(Ticket ticket, string key)
    {
        var plain = CustomFieldRules.StripPrefix(key);
        return (await GetApplicableAsync(ticket.ProjectId, ticket.TypeId)).FirstOrDefault(item => item.Field.Key == plain)
            ?? throw new BusinessException(TicketErrors.UnknownField).WithData("field", CustomFieldConsts.KeyPrefix + plain);
    }

    public static bool HasValue(TicketCustomFieldDto item) =>
        item.Text is not null || item.Number is not null || item.Date is not null || item.Checked is not null || item.User is not null || item.Options.Count > 0;

    /// <summary>Whether the ticket has a value in the field.</summary>
    public async Task<bool> HasValueAsync(Guid ticketId, Guid fieldId) =>
        await values.AnyAsync(value => value.TicketId == ticketId && value.FieldId == fieldId);

    private async Task<(string? Old, string? New)?> WriteAsync(Ticket ticket, ApplicableField applicable, JsonElement value)
    {
        var field = applicable.Field;
        var parsed = CustomFieldRules.Parse(field, value, applicable.Options);
        var rows = await values.GetListAsync(row => row.TicketId == ticket.Id && row.FieldId == field.Id);

        // A hidden option is no longer offered, but a ticket that has it keeps it.
        var current = rows.Where(row => row.OptionId.HasValue).Select(row => row.OptionId!.Value).ToHashSet();
        if (parsed.OptionIds.Any(id => !current.Contains(id) && applicable.Options.Single(option => option.Id == id).IsHidden))
        {
            throw new BusinessException(CustomFieldErrors.OptionInvalid).WithData("field", field.Name);
        }

        if (parsed.UserId is { } userId && !(await userDirectory.GetByIdsAsync([userId])).ContainsKey(userId))
        {
            throw CustomFieldRules.Invalid(field, "user id");
        }

        var before = Display(field, rows, applicable.Options);
        await values.DeleteManyAsync(rows, autoSave: true);
        var next = new List<TicketFieldValue>();
        if (!parsed.IsEmpty)
        {
            IEnumerable<Guid?> optionIds = parsed.OptionIds.Count > 0 ? parsed.OptionIds.Select(id => (Guid?)id) : new Guid?[] { null };
            next.AddRange(optionIds.Select(optionId => new TicketFieldValue(guids.Create(), ticket.Id, field.Id)
            {
                TextValue = parsed.Text,
                NumberValue = parsed.Number,
                DateValue = parsed.Date,
                BoolValue = parsed.Checked,
                UserId = parsed.UserId,
                OptionId = optionId,
            }));
            await values.InsertManyAsync(next, autoSave: true);
        }

        var after = Display(field, next, applicable.Options);
        return before == after ? null : (before, after);
    }

    /// <summary>
    /// What history keeps: the text, number or date; option labels; a user's id, which readers turn into the current
    /// name.
    /// </summary>
    public static string? Display(CustomField field, IReadOnlyCollection<TicketFieldValue> rows, IReadOnlyList<CustomFieldOption> fieldOptions)
    {
        var first = rows.FirstOrDefault();
        if (first is null)
        {
            return null;
        }

        return field.Type switch
        {
            CustomFieldType.Number => first.NumberValue?.ToString("0.####", CultureInfo.InvariantCulture),
            CustomFieldType.Date => first.DateValue?.ToString("O"),
            CustomFieldType.Checkbox => first.BoolValue?.ToString().ToLowerInvariant(),
            CustomFieldType.User => first.UserId?.ToString(),
            CustomFieldType.Select or CustomFieldType.MultiSelect => string.Join(", ", fieldOptions
                .Where(option => rows.Any(row => row.OptionId == option.Id)).Select(option => option.Label)),
            _ => first.TextValue is { Length: > 200 } lengthy ? lengthy[..200] + "…" : first.TextValue,
        };
    }
}
