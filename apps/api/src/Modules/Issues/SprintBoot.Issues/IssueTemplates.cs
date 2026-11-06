using Microsoft.AspNetCore.Authorization;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Issues;

public class IssueTemplate : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public Guid IssueTypeId { get; set; }

    public string Name { get; set; } = null!;

    public string Content { get; set; } = string.Empty;

    public IssueTemplateMode Mode { get; set; }

    /// <summary>A reused workspace template. Name, content and mode are kept as copies of it.</summary>
    public Guid? GlobalTemplateId { get; set; }

    public bool IsDefault { get; set; }

    protected IssueTemplate()
    {
    }

    public IssueTemplate(Guid id, Guid projectId)
        : base(id)
    {
        ProjectId = projectId;
    }
}

/// <summary>A template the workspace keeps for every space to reuse.</summary>
public class GlobalIssueTemplate : Entity<Guid>
{
    public string Name { get; set; } = null!;

    public string Content { get; set; } = string.Empty;

    public IssueTemplateMode Mode { get; set; }

    protected GlobalIssueTemplate()
    {
    }

    public GlobalIssueTemplate(Guid id)
        : base(id)
    {
    }
}

/// <summary>Templates belong to one space; its admins manage them, its readers use them.</summary>
/// <remarks>
/// The template editor offers no uploads, so templates never own rich text files: an issue made from one
/// could not keep a file the template later drops.
/// </remarks>
[Authorize]
public class IssueTemplateAppService(
    IRepository<IssueTemplate, Guid> templates,
    IRepository<GlobalIssueTemplate, Guid> globals,
    IRepository<IssueType, Guid> types,
    IProjectAccess projectAccess,
    RichTextSanitizer sanitizer)
    : ApplicationService, IIssueTemplateAppService
{
    public async Task<List<IssueTemplateDto>> GetListAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        var list = await templates.GetListAsync(template => template.ProjectId == projectId);
        return [.. list.OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase).Select(ToDto)];
    }

    public async Task<IssueTemplateDto> CreateAsync(Guid projectId, SaveIssueTemplateInput input)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.Manage);
        var template = new IssueTemplate(GuidGenerator.Create(), projectId);
        await ApplyAsync(template, input);
        await templates.InsertAsync(template, autoSave: true);
        return ToDto(template);
    }

    public async Task<IssueTemplateDto> UpdateAsync(Guid id, SaveIssueTemplateInput input)
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

    private async Task ApplyAsync(IssueTemplate template, SaveIssueTemplateInput input)
    {
        var type = await types.FirstOrDefaultAsync(item => item.Id == input.IssueTypeId && item.ProjectId == template.ProjectId)
            ?? throw new BusinessException(IssueErrors.ProjectMismatch);
        if (type.Hierarchy == IssueHierarchy.Subtask)
        {
            throw new BusinessException(IssueTemplateErrors.SubtaskType);
        }

        template.IssueTypeId = type.Id;
        template.IsDefault = input.IsDefault;
        if (input.GlobalTemplateId is { } globalId)
        {
            // Copies, kept in step by the workspace service, so reading a space needs no join.
            var source = await globals.GetAsync(globalId);
            template.GlobalTemplateId = source.Id;
            template.Name = source.Name;
            template.Content = source.Content;
            template.Mode = source.Mode;
        }
        else
        {
            template.GlobalTemplateId = null;
            template.Name = GlobalIssueTemplateAppService.CheckName(input.Name);
            template.Content = sanitizer.Clean(input.Content).Html;
            template.Mode = GlobalIssueTemplateAppService.CheckMode(input.Mode);
        }

        if (input.IsDefault)
        {
            // Only one default per type: choosing a new one quietly retires the old.
            var others = await templates.GetListAsync(item =>
                item.ProjectId == template.ProjectId && item.IssueTypeId == type.Id && item.IsDefault && item.Id != template.Id);
            foreach (var other in others)
            {
                other.IsDefault = false;
                await templates.UpdateAsync(other);
            }
        }
    }

    private static IssueTemplateDto ToDto(IssueTemplate template) => new()
    {
        Id = template.Id,
        ProjectId = template.ProjectId,
        IssueTypeId = template.IssueTypeId,
        Name = template.Name,
        Content = template.Content,
        Mode = template.Mode,
        GlobalTemplateId = template.GlobalTemplateId,
        IsDefault = template.IsDefault,
    };
}

/// <summary>Workspace admins keep these; space admins reuse them for their own issue types.</summary>
[Authorize]
public class GlobalIssueTemplateAppService(
    IRepository<GlobalIssueTemplate, Guid> globals,
    IRepository<IssueTemplate, Guid> templates,
    RichTextSanitizer sanitizer)
    : ApplicationService, IGlobalIssueTemplateAppService
{
    public async Task<List<GlobalIssueTemplateDto>> GetListAsync()
    {
        var list = await globals.GetListAsync();
        var queryable = await templates.GetQueryableAsync();
        var usage = await AsyncExecuter.ToListAsync(queryable
            .Where(template => template.GlobalTemplateId != null)
            .GroupBy(template => template.GlobalTemplateId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() }));
        var counts = usage.ToDictionary(item => item.Id, item => item.Count);
        return [.. list
            .OrderBy(global => global.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(global => ToDto(global, counts.GetValueOrDefault(global.Id)))];
    }

    [Authorize(SprintBootPermissions.ManageIssueTemplates)]
    public async Task<GlobalIssueTemplateDto> CreateAsync(SaveGlobalIssueTemplateInput input)
    {
        var global = new GlobalIssueTemplate(GuidGenerator.Create());
        Apply(global, input);
        await globals.InsertAsync(global, autoSave: true);
        return ToDto(global, 0);
    }

    [Authorize(SprintBootPermissions.ManageIssueTemplates)]
    public async Task<GlobalIssueTemplateDto> UpdateAsync(Guid id, SaveGlobalIssueTemplateInput input)
    {
        var global = await globals.GetAsync(id);
        Apply(global, input);
        await globals.UpdateAsync(global);

        // Spaces that reuse it follow every change.
        var reusing = await templates.GetListAsync(template => template.GlobalTemplateId == id);
        foreach (var template in reusing)
        {
            template.Name = global.Name;
            template.Content = global.Content;
            template.Mode = global.Mode;
        }

        await templates.UpdateManyAsync(reusing, autoSave: true);
        return ToDto(global, reusing.Count);
    }

    [Authorize(SprintBootPermissions.ManageIssueTemplates)]
    public async Task DeleteAsync(Guid id)
    {
        var global = await globals.GetAsync(id);
        await templates.DeleteAsync(template => template.GlobalTemplateId == id);
        await globals.DeleteAsync(global, autoSave: true);
    }

    private void Apply(GlobalIssueTemplate global, SaveGlobalIssueTemplateInput input)
    {
        global.Name = CheckName(input.Name);
        global.Content = sanitizer.Clean(input.Content).Html;
        global.Mode = CheckMode(input.Mode);
    }

    internal static string CheckName(string? input)
    {
        var name = input?.Trim();
        return string.IsNullOrEmpty(name) ? throw new BusinessException(IssueTemplateErrors.NameRequired) : name;
    }

    internal static IssueTemplateMode CheckMode(IssueTemplateMode mode) =>
        Enum.IsDefined(mode) ? mode : throw new BusinessException(IssueTemplateErrors.InvalidMode);

    private static GlobalIssueTemplateDto ToDto(GlobalIssueTemplate global, int usage) => new()
    {
        Id = global.Id,
        Name = global.Name,
        Content = global.Content,
        Mode = global.Mode,
        UsageCount = usage,
    };
}
