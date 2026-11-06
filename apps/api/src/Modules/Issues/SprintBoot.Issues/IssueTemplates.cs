using Microsoft.AspNetCore.Authorization;
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

/// <summary>Templates belong to one space; its admins manage them, its readers use them.</summary>
/// <remarks>
/// The template editor offers no uploads, so templates never own rich text files: an issue made from one
/// could not keep a file the template later drops.
/// </remarks>
[Authorize]
public class IssueTemplateAppService(
    IRepository<IssueTemplate, Guid> templates,
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

        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new BusinessException(IssueTemplateErrors.NameRequired);
        }

        template.IssueTypeId = type.Id;
        template.Name = name;
        template.Content = sanitizer.Clean(input.Content).Html;
        template.IsDefault = input.IsDefault;

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
        IsDefault = template.IsDefault,
    };
}
