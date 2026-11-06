using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Services;

namespace SprintBoot.Issues;

/// <summary>How the create form shows a template.</summary>
public enum IssueTemplateMode
{
    /// <summary>The template becomes the description, ready to edit.</summary>
    Fill = 0,

    /// <summary>The template shows as a hint in the empty description and is never saved with the issue.</summary>
    Placeholder = 1,
}

/// <summary>A description a space fills in when someone creates an issue of one type.</summary>
public class IssueTemplateDto
{
    public required Guid Id { get; set; }

    public required Guid ProjectId { get; set; }

    public required Guid IssueTypeId { get; set; }

    public required string Name { get; set; }

    /// <summary>Sanitized rich text HTML.</summary>
    public required string Content { get; set; }

    public required IssueTemplateMode Mode { get; set; }

    /// <summary>
    /// Set when the space reuses a workspace template: name, content and mode then come from it and follow its
    /// changes.
    /// </summary>
    public Guid? GlobalTemplateId { get; set; }

    /// <summary>The one template of its type that new issues start with.</summary>
    public required bool IsDefault { get; set; }
}

public class SaveIssueTemplateInput
{
    public required Guid IssueTypeId { get; set; }

    /// <summary>Reuse a workspace template; name, content and mode are then ignored.</summary>
    public Guid? GlobalTemplateId { get; set; }

    /// <summary>Required unless a workspace template is reused.</summary>
    [StringLength(IssueTemplateConsts.MaxNameLength)]
    public string? Name { get; set; }

    public string? Content { get; set; }

    public IssueTemplateMode Mode { get; set; }

    public bool IsDefault { get; set; }
}

/// <summary>A template workspace admins keep for every space to reuse.</summary>
public class GlobalIssueTemplateDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Sanitized rich text HTML.</summary>
    public required string Content { get; set; }

    public required IssueTemplateMode Mode { get; set; }

    /// <summary>How many space templates reuse it; deleting it removes them too.</summary>
    public required int UsageCount { get; set; }
}

public class SaveGlobalIssueTemplateInput
{
    [Required]
    [StringLength(IssueTemplateConsts.MaxNameLength)]
    public required string Name { get; set; }

    public string? Content { get; set; }

    public IssueTemplateMode Mode { get; set; }
}

public static class IssueTemplateConsts
{
    public const int MaxNameLength = 128;
}

public static class IssueTemplateErrors
{
    public const string SubtaskType = "SprintBoot:IssueTemplateSubtaskType";
    public const string NameRequired = "SprintBoot:IssueTemplateNameRequired";
    public const string InvalidMode = "SprintBoot:IssueTemplateInvalidMode";
}

public interface IIssueTemplateAppService : IApplicationService
{
    /// <summary>Everyone who can see the space's issues reads them, so the create form can fill in the default.</summary>
    Task<List<IssueTemplateDto>> GetListAsync(Guid projectId);

    Task<IssueTemplateDto> CreateAsync(Guid projectId, SaveIssueTemplateInput input);

    Task<IssueTemplateDto> UpdateAsync(Guid id, SaveIssueTemplateInput input);

    Task DeleteAsync(Guid id);
}

/// <summary>Workspace templates: anyone signed in reads them, so space admins can pick one to reuse.</summary>
public interface IGlobalIssueTemplateAppService : IApplicationService
{
    Task<List<GlobalIssueTemplateDto>> GetListAsync();

    Task<GlobalIssueTemplateDto> CreateAsync(SaveGlobalIssueTemplateInput input);

    Task<GlobalIssueTemplateDto> UpdateAsync(Guid id, SaveGlobalIssueTemplateInput input);

    /// <summary>Also removes the space templates that reuse it; issues made from them keep their text.</summary>
    Task DeleteAsync(Guid id);
}
