using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Tickets;

public class TicketTemplate : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public Guid TicketTypeId { get; set; }

    public string Name { get; set; } = null!;

    public string Content { get; set; } = string.Empty;

    public TicketTemplateMode Mode { get; set; }

    /// <summary>A reused workspace template. Name, content and mode are kept as copies of it.</summary>
    public Guid? GlobalTemplateId { get; set; }

    public bool IsDefault { get; set; }

    /// <summary>Custom field values a new ticket starts with, as a JSON object by field key.</summary>
    public string? CustomFieldValues { get; set; }

    protected TicketTemplate()
    {
    }

    public TicketTemplate(Guid id, Guid projectId)
        : base(id)
    {
        ProjectId = projectId;
    }
}

/// <summary>A template the workspace keeps for every space to reuse.</summary>
public class GlobalTicketTemplate : Entity<Guid>
{
    /// <summary>The <see cref="TicketType.Icon"/> of the space types it is for.</summary>
    public string TicketTypeKey { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Content { get; set; } = string.Empty;

    public TicketTemplateMode Mode { get; set; }

    protected GlobalTicketTemplate()
    {
    }

    public GlobalTicketTemplate(Guid id)
        : base(id)
    {
    }
}

/// <summary>Templates belong to one space; its admins manage them, its readers use them.</summary>
/// <remarks>
/// The template editor offers no uploads, so templates never own rich text files: a ticket made from one
/// could not keep a file the template later drops.
/// </remarks>
[Authorize]
public class TicketTemplateAppService(
    IRepository<TicketTemplate, Guid> templates,
    IRepository<GlobalTicketTemplate, Guid> globals,
    IRepository<TicketType, Guid> types,
    IProjectAccess projectAccess,
    RichTextSanitizer sanitizer,
    CustomFieldStore customFieldStore)
    : ApplicationService, ITicketTemplateAppService
{
    public async Task<List<TicketTemplateDto>> GetListAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);
        var list = await templates.GetListAsync(template => template.ProjectId == projectId);
        return [.. list.OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase).Select(ToDto)];
    }

    public async Task<TicketTemplateDto> CreateAsync(Guid projectId, SaveTicketTemplateInput input)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.Manage);
        var template = new TicketTemplate(GuidGenerator.Create(), projectId);
        await ApplyAsync(template, input);
        await templates.InsertAsync(template, autoSave: true);
        return ToDto(template);
    }

    public async Task<TicketTemplateDto> UpdateAsync(Guid id, SaveTicketTemplateInput input)
    {
        var template = await templates.GetAsync(id);
        await projectAccess.CheckAsync(template.ProjectId, ProjectPermissions.Manage);
        await ApplyAsync(template, input);
        await templates.UpdateAsync(template, autoSave: true);
        return ToDto(template);
    }

    public async Task DeleteAsync(Guid id)
    {
        var template = await templates.GetAsync(id);
        await projectAccess.CheckAsync(template.ProjectId, ProjectPermissions.Manage);
        await templates.DeleteAsync(template);
    }

    private async Task ApplyAsync(TicketTemplate template, SaveTicketTemplateInput input)
    {
        var type = await types.FirstOrDefaultAsync(item => item.Id == input.TicketTypeId && item.ProjectId == template.ProjectId)
            ?? throw new BusinessException(TicketErrors.ProjectMismatch);
        if (type.Hierarchy == TicketHierarchy.Subtask)
        {
            throw new BusinessException(TicketTemplateErrors.SubtaskType);
        }

        template.TicketTypeId = type.Id;
        template.IsDefault = input.IsDefault;
        template.CustomFieldValues = await CheckCustomFieldsAsync(template.ProjectId, type.Id, input.CustomFields);
        if (input.GlobalTemplateId is { } globalId)
        {
            // Copies, kept in step by the workspace service, so reading a space needs no join.
            var source = await globals.GetAsync(globalId);
            if (source.TicketTypeKey != type.Icon)
            {
                throw new BusinessException(TicketTemplateErrors.TypeMismatch);
            }

            template.GlobalTemplateId = source.Id;
            template.Name = source.Name;
            template.Content = source.Content;
            template.Mode = source.Mode;
        }
        else
        {
            template.GlobalTemplateId = null;
            template.Name = GlobalTicketTemplateAppService.CheckName(input.Name);
            template.Content = sanitizer.Clean(input.Content).Html;
            template.Mode = GlobalTicketTemplateAppService.CheckMode(input.Mode);
        }

        if (input.IsDefault)
        {
            // Only one default per type: choosing a new one quietly retires the old.
            var others = await templates.GetListAsync(item =>
                item.ProjectId == template.ProjectId && item.TicketTypeId == type.Id && item.IsDefault && item.Id != template.Id);
            foreach (var other in others)
            {
                other.IsDefault = false;
                await templates.UpdateAsync(other);
            }
        }
    }

    /// <summary>Only fields the space shows for the type, each value checked like on a ticket; stored by key.</summary>
    private async Task<string?> CheckCustomFieldsAsync(Guid projectId, Guid typeId, Dictionary<string, JsonElement>? given)
    {
        if (given is not { Count: > 0 })
        {
            return null;
        }

        var applicable = await customFieldStore.GetApplicableAsync(projectId, typeId);
        var kept = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in given)
        {
            var plain = CustomFieldRules.StripPrefix(key);
            var item = applicable.FirstOrDefault(candidate => candidate.Field.Key == plain)
                ?? throw new BusinessException(TicketErrors.UnknownField).WithData("field", CustomFieldConsts.KeyPrefix + plain);
            if (!CustomFieldRules.Parse(item.Field, value, item.Options).IsEmpty)
            {
                kept[plain] = value;
            }
        }

        return kept.Count == 0 ? null : JsonSerializer.Serialize(kept);
    }

    private static TicketTemplateDto ToDto(TicketTemplate template) => new()
    {
        Id = template.Id,
        ProjectId = template.ProjectId,
        TicketTypeId = template.TicketTypeId,
        Name = template.Name,
        Content = template.Content,
        Mode = template.Mode,
        GlobalTemplateId = template.GlobalTemplateId,
        IsDefault = template.IsDefault,
        CustomFields = template.CustomFieldValues is { } json ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) : null,
    };
}

/// <summary>Workspace admins keep these; space admins reuse them for their own ticket types.</summary>
[Authorize]
public class GlobalTicketTemplateAppService(
    IRepository<GlobalTicketTemplate, Guid> globals,
    IRepository<TicketTemplate, Guid> templates,
    RichTextSanitizer sanitizer)
    : ApplicationService, IGlobalTicketTemplateAppService
{
    public async Task<List<GlobalTicketTemplateDto>> GetListAsync()
    {
        var list = await globals.GetListAsync();
        var queryable = await templates.GetQueryableAsync();
        var usage = await AsyncExecuter.ToListAsync(queryable
            .Where(template => template.GlobalTemplateId != null)
            .GroupBy(template => template.GlobalTemplateId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() }));
        var counts = usage.ToDictionary(item => item.Id, item => item.Count);
        return [.. list
            .OrderBy(global => Array.IndexOf(TicketTemplateConsts.TypeKeys, global.TicketTypeKey))
            .ThenBy(global => global.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(global => ToDto(global, counts.GetValueOrDefault(global.Id)))];
    }

    [Authorize(SprintBootPermissions.ManageTicketTemplates)]
    public async Task<GlobalTicketTemplateDto> CreateAsync(SaveGlobalTicketTemplateInput input)
    {
        var global = new GlobalTicketTemplate(GuidGenerator.Create());
        global.TicketTypeKey = CheckTypeKey(input.TicketTypeKey);
        Apply(global, input);
        await globals.InsertAsync(global, autoSave: true);
        return ToDto(global, 0);
    }

    [Authorize(SprintBootPermissions.ManageTicketTemplates)]
    public async Task<GlobalTicketTemplateDto> UpdateAsync(Guid id, SaveGlobalTicketTemplateInput input)
    {
        var global = await globals.GetAsync(id);
        var reusing = await templates.GetListAsync(template => template.GlobalTemplateId == id);
        var typeKey = CheckTypeKey(input.TicketTypeKey);
        if (typeKey != global.TicketTypeKey && reusing.Count > 0)
        {
            // Spaces picked it for one of their types; moving it would leave those templates on the wrong type.
            throw new BusinessException(TicketTemplateErrors.TypeInUse);
        }

        global.TicketTypeKey = typeKey;
        Apply(global, input);
        await globals.UpdateAsync(global);

        // Spaces that reuse it follow every change.
        foreach (var template in reusing)
        {
            template.Name = global.Name;
            template.Content = global.Content;
            template.Mode = global.Mode;
        }

        await templates.UpdateManyAsync(reusing, autoSave: true);
        return ToDto(global, reusing.Count);
    }

    [Authorize(SprintBootPermissions.ManageTicketTemplates)]
    public async Task DeleteAsync(Guid id)
    {
        var global = await globals.GetAsync(id);
        await templates.DeleteAsync(template => template.GlobalTemplateId == id);
        await globals.DeleteAsync(global, autoSave: true);
    }

    private void Apply(GlobalTicketTemplate global, SaveGlobalTicketTemplateInput input)
    {
        global.Name = CheckName(input.Name);
        global.Content = sanitizer.Clean(input.Content).Html;
        global.Mode = CheckMode(input.Mode);
    }

    internal static string CheckName(string? input)
    {
        var name = input?.Trim();
        return string.IsNullOrEmpty(name) ? throw new BusinessException(TicketTemplateErrors.NameRequired) : name;
    }

    private static string CheckTypeKey(string? input)
    {
        var key = input?.Trim().ToLowerInvariant();
        return key is not null && TicketTemplateConsts.TypeKeys.Contains(key) ? key : throw new BusinessException(TicketTemplateErrors.InvalidType);
    }

    internal static TicketTemplateMode CheckMode(TicketTemplateMode mode) =>
        Enum.IsDefined(mode) ? mode : throw new BusinessException(TicketTemplateErrors.InvalidMode);

    private static GlobalTicketTemplateDto ToDto(GlobalTicketTemplate global, int usage) => new()
    {
        Id = global.Id,
        TicketTypeKey = global.TicketTypeKey,
        Name = global.Name,
        Content = global.Content,
        Mode = global.Mode,
        UsageCount = usage,
    };
}
