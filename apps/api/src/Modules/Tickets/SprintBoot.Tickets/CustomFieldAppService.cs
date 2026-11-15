using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Tickets;

/// <summary>
/// Workspace fields belong to admins with <see cref="SprintBootPermissions.ManageCustomFields"/>; a space's own fields,
/// and which fields it turns on for which ticket types, to the people who manage the space.
/// </summary>
[Authorize]
public class CustomFieldAppService(
    IRepository<CustomField, Guid> fields,
    IRepository<CustomFieldOption, Guid> options,
    IRepository<ProjectCustomField, Guid> spaceFields,
    IRepository<TicketFieldValue, Guid> values,
    IRepository<TicketType, Guid> types,
    IProjectAccess projectAccess)
    : ApplicationService, ICustomFieldAppService
{
    public async Task<List<CustomFieldDto>> GetListAsync(Guid? projectId)
    {
        await CheckManageAsync(projectId);
        var list = await fields.GetListAsync(field => field.ProjectId == projectId);
        return await ToDtosAsync(list);
    }

    public async Task<CustomFieldDto> CreateAsync(CreateCustomFieldInput input)
    {
        await CheckManageAsync(input.ProjectId);
        if (!Enum.IsDefined(input.Type))
        {
            throw new BusinessException(CustomFieldErrors.TypeMismatch);
        }

        var name = CheckName(input.Name);
        var field = new CustomField(GuidGenerator.Create(), input.ProjectId, await FreeKeyAsync(input.ProjectId, name), name, input.Type)
        {
            Description = Clean(input.Description),
        };
        await fields.InsertAsync(field, autoSave: true);
        await SaveOptionsAsync(field, input.Options);
        field.DefaultValue = await CheckDefaultAsync(field, input.DefaultValue);
        await fields.UpdateAsync(field, autoSave: true);

        // A space's own field is on in the space at once, for every ticket type but subtasks.
        if (input.ProjectId is { } projectId)
        {
            var typeIds = (await types.GetListAsync(type => type.ProjectId == projectId && type.Hierarchy != TicketHierarchy.Subtask)).Select(type => type.Id).ToList();
            var order = (await spaceFields.GetListAsync(row => row.ProjectId == projectId)).Select(row => row.Order).DefaultIfEmpty(-1).Max() + 1;
            await spaceFields.InsertAsync(new ProjectCustomField(GuidGenerator.Create(), projectId, field.Id) { Order = order, TicketTypeIds = typeIds }, autoSave: true);
        }

        return (await ToDtosAsync([field])).Single();
    }

    public async Task<CustomFieldDto> UpdateAsync(Guid id, UpdateCustomFieldInput input)
    {
        var field = await fields.GetAsync(id);
        await CheckManageAsync(field.ProjectId);
        field.Name = CheckName(input.Name);
        field.Description = Clean(input.Description);
        await SaveOptionsAsync(field, input.Options);
        field.DefaultValue = await CheckDefaultAsync(field, input.DefaultValue);
        await fields.UpdateAsync(field, autoSave: true);
        return (await ToDtosAsync([field])).Single();
    }

    public async Task DeleteAsync(Guid id)
    {
        var field = await fields.GetAsync(id);
        await CheckManageAsync(field.ProjectId);
        await spaceFields.DeleteAsync(row => row.FieldId == id);
        await fields.DeleteAsync(field);
    }

    public async Task<List<SpaceCustomFieldDto>> GetSpaceFieldsAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);
        var rows = await spaceFields.GetListAsync(row => row.ProjectId == projectId);
        var list = await fields.GetListAsync(field => field.ProjectId == null || field.ProjectId == projectId);
        var dtos = (await ToDtosAsync(list)).ToDictionary(dto => dto.Id);

        var on = rows.OrderBy(row => row.Order).Where(row => dtos.ContainsKey(row.FieldId))
            .Select(row => new SpaceCustomFieldDto { Field = dtos[row.FieldId], Enabled = true, TicketTypeIds = [.. row.TicketTypeIds] });
        var off = list.Where(field => rows.All(row => row.FieldId != field.Id)).OrderBy(field => field.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(field => new SpaceCustomFieldDto { Field = dtos[field.Id], Enabled = false, TicketTypeIds = [] });
        return [.. on, .. off];
    }

    public async Task<List<SpaceCustomFieldDto>> SetSpaceFieldsAsync(Guid projectId, SaveSpaceCustomFieldsInput input)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.Manage);
        var available = (await fields.GetListAsync(field => field.ProjectId == null || field.ProjectId == projectId)).ToDictionary(field => field.Id);
        var typeIds = (await types.GetListAsync(type => type.ProjectId == projectId)).Select(type => type.Id).ToHashSet();
        var items = input.Items.DistinctBy(item => item.FieldId).ToList();
        if (items.Any(item => !available.ContainsKey(item.FieldId) || item.TicketTypeIds.Any(id => !typeIds.Contains(id))))
        {
            throw new BusinessException(CustomFieldErrors.NotInSpace);
        }

        // The space's own fields stay on even when left out; only workspace fields can be turned off.
        var own = available.Values.Where(field => field.ProjectId == projectId && items.All(item => item.FieldId != field.Id))
            .Select(field => new SpaceCustomFieldInput { FieldId = field.Id, TicketTypeIds = [] });
        var rows = (await spaceFields.GetListAsync(row => row.ProjectId == projectId)).ToDictionary(row => row.FieldId);
        var order = 0;
        foreach (var item in items.Concat(own))
        {
            var row = rows.GetValueOrDefault(item.FieldId);
            if (row is null)
            {
                row = new ProjectCustomField(GuidGenerator.Create(), projectId, item.FieldId);
                rows[item.FieldId] = row;
                await spaceFields.InsertAsync(row);
            }

            row.Order = order++;
            row.TicketTypeIds = [.. item.TicketTypeIds.Distinct()];
            await spaceFields.UpdateAsync(row);
        }

        var kept = items.Select(item => item.FieldId).Concat(own.Select(item => item.FieldId)).ToHashSet();
        await spaceFields.DeleteManyAsync(rows.Values.Where(row => !kept.Contains(row.FieldId)));
        await CurrentUnitOfWork!.SaveChangesAsync();
        return await GetSpaceFieldsAsync(projectId);
    }

    private async Task CheckManageAsync(Guid? projectId)
    {
        if (projectId is { } id)
        {
            await projectAccess.CheckAsync(id, ProjectPermissions.Manage);
        }
        else
        {
            await AuthorizationService.CheckAsync(SprintBootPermissions.ManageCustomFields);
        }
    }

    private static string CheckName(string? name) =>
        name?.Trim() is { Length: > 0 } trimmed ? trimmed : throw new BusinessException(CustomFieldErrors.NameRequired);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// A key no field of the same reach uses: a workspace key is unique among every field, a space key among the
    /// workspace's and its own, so a space never sees two fields of one key.
    /// </summary>
    private async Task<string> FreeKeyAsync(Guid? projectId, string name)
    {
        var stem = CustomFieldRules.KeyFrom(name);
        var query = await fields.GetQueryableAsync();
        var taken = (await AsyncExecuter.ToListAsync((projectId is null
                ? query.Where(field => field.Key.StartsWith(stem))
                : query.Where(field => (field.ProjectId == null || field.ProjectId == projectId) && field.Key.StartsWith(stem)))
            .Select(field => field.Key))).ToHashSet();
        var key = stem;
        for (var number = 2; taken.Contains(key); number++)
        {
            key = $"{stem}_{number}";
        }

        return key;
    }

    /// <summary>Options in the given order. An option left out that tickets use is hidden instead of removed.</summary>
    private async Task SaveOptionsAsync(CustomField field, List<SaveCustomFieldOptionInput>? input)
    {
        if (!CustomFieldRules.HasOptions(field.Type))
        {
            return;
        }

        var wanted = (input ?? []).Where(option => !string.IsNullOrWhiteSpace(option.Label)).ToList();
        if (wanted.Count == 0)
        {
            throw new BusinessException(CustomFieldErrors.OptionsRequired);
        }

        if (wanted.Count > CustomFieldConsts.MaxOptions || wanted.Any(option => option.Color is { } color && !CustomFieldRules.Colors.Contains(color)))
        {
            throw new BusinessException(CustomFieldErrors.OptionInvalid).WithData("field", field.Name);
        }

        var existing = (await options.GetListAsync(option => option.FieldId == field.Id)).ToDictionary(option => option.Id);
        var order = 0;
        foreach (var item in wanted)
        {
            var option = item.Id is { } id && existing.Remove(id, out var found) ? found : null;
            if (option is null)
            {
                option = new CustomFieldOption(GuidGenerator.Create(), field.Id, item.Label.Trim());
                await options.InsertAsync(option);
            }

            option.Label = item.Label.Trim();
            option.Color = item.Color;
            option.IsHidden = item.IsHidden;
            option.Order = order++;
            await options.UpdateAsync(option);
        }

        var removedIds = existing.Keys.ToList();
        var used = (await values.GetListAsync(value => value.OptionId != null && removedIds.Contains(value.OptionId.Value)))
            .Select(value => value.OptionId!.Value).ToHashSet();
        foreach (var option in existing.Values)
        {
            if (used.Contains(option.Id))
            {
                option.IsHidden = true;
                option.Order = order++;
                await options.UpdateAsync(option);
            }
            else
            {
                await options.DeleteAsync(option);
            }
        }

        await CurrentUnitOfWork!.SaveChangesAsync();
    }

    /// <summary>The default, checked like any value; stored as compact JSON.</summary>
    private async Task<string?> CheckDefaultAsync(CustomField field, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        if (field.Type is CustomFieldType.User)
        {
            throw CustomFieldRules.Invalid(field, "no default");
        }

        JsonElement value;
        try
        {
            using var document = JsonDocument.Parse(json);
            value = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw CustomFieldRules.Invalid(field, "JSON");
        }

        var fieldOptions = await options.GetListAsync(option => option.FieldId == field.Id);
        var parsed = CustomFieldRules.Parse(field, value, fieldOptions);
        if (parsed.IsEmpty)
        {
            return null;
        }

        // Options by id, so renaming one keeps the default.
        return CustomFieldRules.HasOptions(field.Type)
            ? JsonSerializer.Serialize(field.Type == CustomFieldType.Select ? (object)parsed.OptionIds[0] : parsed.OptionIds)
            : JsonSerializer.Serialize(value);
    }

    private async Task<List<CustomFieldDto>> ToDtosAsync(List<CustomField> list)
    {
        var ids = list.Select(field => field.Id).ToList();
        var choices = (await options.GetListAsync(option => ids.Contains(option.FieldId))).ToLookup(option => option.FieldId);
        var usage = (await AsyncExecuter.ToListAsync((await values.GetQueryableAsync()).Where(value => ids.Contains(value.FieldId))
            .Select(value => new { value.FieldId, value.TicketId }).Distinct()
            .GroupBy(value => value.FieldId).Select(group => new { group.Key, Count = group.Count() })))
            .ToDictionary(item => item.Key, item => item.Count);
        return [.. list.OrderBy(field => field.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(field => CustomFieldStore.ToDto(field, choices[field.Id], usage.GetValueOrDefault(field.Id)))];
    }
}
