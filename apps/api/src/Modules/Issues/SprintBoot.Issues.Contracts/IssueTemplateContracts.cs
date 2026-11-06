using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Services;

namespace SprintBoot.Issues;

/// <summary>A description a space fills in when someone creates an issue of one type.</summary>
public class IssueTemplateDto
{
    public required Guid Id { get; set; }

    public required Guid ProjectId { get; set; }

    public required Guid IssueTypeId { get; set; }

    public required string Name { get; set; }

    /// <summary>Sanitized rich text HTML.</summary>
    public required string Content { get; set; }

    /// <summary>The one template of its type that new issues start with.</summary>
    public required bool IsDefault { get; set; }
}

public class SaveIssueTemplateInput
{
    public required Guid IssueTypeId { get; set; }

    [Required]
    [StringLength(IssueTemplateConsts.MaxNameLength)]
    public required string Name { get; set; }

    public string? Content { get; set; }

    public bool IsDefault { get; set; }
}

public static class IssueTemplateConsts
{
    public const int MaxNameLength = 128;
}

public static class IssueTemplateErrors
{
    public const string SubtaskType = "SprintBoot:IssueTemplateSubtaskType";
    public const string NameRequired = "SprintBoot:IssueTemplateNameRequired";
}

public interface IIssueTemplateAppService : IApplicationService
{
    /// <summary>Everyone who can see the space's issues reads them, so the create form can fill in the default.</summary>
    Task<List<IssueTemplateDto>> GetListAsync(Guid projectId);

    Task<IssueTemplateDto> CreateAsync(Guid projectId, SaveIssueTemplateInput input);

    Task<IssueTemplateDto> UpdateAsync(Guid id, SaveIssueTemplateInput input);

    Task DeleteAsync(Guid id);
}
