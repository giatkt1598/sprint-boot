using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Volo.Abp.Application.Services;

namespace SprintBoot.Tickets;

/// <summary>How the create form shows a template.</summary>
public enum TicketTemplateMode
{
    /// <summary>The template becomes the description, ready to edit.</summary>
    Fill = 0,

    /// <summary>The template shows as a hint in the empty description and is never saved with the ticket.</summary>
    Placeholder = 1,
}

/// <summary>A description a space fills in when someone creates a ticket of one type.</summary>
public class TicketTemplateDto
{
    public required Guid Id { get; set; }

    public required Guid ProjectId { get; set; }

    public required Guid TicketTypeId { get; set; }

    public required string Name { get; set; }

    /// <summary>Sanitized rich text HTML.</summary>
    public required string Content { get; set; }

    public required TicketTemplateMode Mode { get; set; }

    /// <summary>
    /// Set when the space reuses a workspace template: name, content and mode then come from it and follow its
    /// changes.
    /// </summary>
    public Guid? GlobalTemplateId { get; set; }

    /// <summary>The one template of its type that new tickets start with.</summary>
    public required bool IsDefault { get; set; }

    /// <summary>Custom field values a ticket made from it starts with, by field key.</summary>
    public Dictionary<string, JsonElement>? CustomFields { get; set; }
}

public class SaveTicketTemplateInput
{
    public required Guid TicketTypeId { get; set; }

    /// <summary>Reuse a workspace template; name, content and mode are then ignored.</summary>
    public Guid? GlobalTemplateId { get; set; }

    /// <summary>Required unless a workspace template is reused.</summary>
    [StringLength(TicketTemplateConsts.MaxNameLength)]
    public string? Name { get; set; }

    public string? Content { get; set; }

    public TicketTemplateMode Mode { get; set; }

    public bool IsDefault { get; set; }

    /// <summary>Values for the custom fields the space shows for the type, by key, as the ticket API takes them.</summary>
    public Dictionary<string, JsonElement>? CustomFields { get; set; }
}

/// <summary>A template workspace admins keep for every space to reuse.</summary>
public class GlobalTicketTemplateDto
{
    public required Guid Id { get; set; }

    /// <summary>The ticket type it is for, by key: epic, story, task or bug. Spaces use it for their type of that key.</summary>
    public required string TicketTypeKey { get; set; }

    public required string Name { get; set; }

    /// <summary>Sanitized rich text HTML.</summary>
    public required string Content { get; set; }

    public required TicketTemplateMode Mode { get; set; }

    /// <summary>How many space templates reuse it; deleting it removes them too.</summary>
    public required int UsageCount { get; set; }
}

public class SaveGlobalTicketTemplateInput
{
    /// <summary>epic, story, task or bug. It cannot change while spaces use the template.</summary>
    [Required]
    [StringLength(TicketTemplateConsts.MaxTypeKeyLength)]
    public required string TicketTypeKey { get; set; }

    [Required]
    [StringLength(TicketTemplateConsts.MaxNameLength)]
    public required string Name { get; set; }

    public string? Content { get; set; }

    public TicketTemplateMode Mode { get; set; }
}

public static class TicketTemplateConsts
{
    public const int MaxNameLength = 128;

    public const int MaxTypeKeyLength = 32;

    /// <summary>The keys of the ticket types every space has and templates may target; subtasks take none.</summary>
    public static readonly string[] TypeKeys = ["epic", "story", "task", "bug"];
}

public static class TicketTemplateErrors
{
    public const string SubtaskType = "SprintBoot:TicketTemplateSubtaskType";
    public const string NameRequired = "SprintBoot:TicketTemplateNameRequired";
    public const string InvalidMode = "SprintBoot:TicketTemplateInvalidMode";
    public const string InvalidType = "SprintBoot:TicketTemplateInvalidType";
    public const string TypeMismatch = "SprintBoot:TicketTemplateTypeMismatch";
    public const string TypeInUse = "SprintBoot:TicketTemplateTypeInUse";
}

public interface ITicketTemplateAppService : IApplicationService
{
    /// <summary>Everyone who can see the space's tickets reads them, so the create form can fill in the default.</summary>
    Task<List<TicketTemplateDto>> GetListAsync(Guid projectId);

    Task<TicketTemplateDto> CreateAsync(Guid projectId, SaveTicketTemplateInput input);

    Task<TicketTemplateDto> UpdateAsync(Guid id, SaveTicketTemplateInput input);

    Task DeleteAsync(Guid id);
}

/// <summary>Workspace templates: anyone signed in reads them, so space admins can pick one to reuse.</summary>
public interface IGlobalTicketTemplateAppService : IApplicationService
{
    Task<List<GlobalTicketTemplateDto>> GetListAsync();

    Task<GlobalTicketTemplateDto> CreateAsync(SaveGlobalTicketTemplateInput input);

    Task<GlobalTicketTemplateDto> UpdateAsync(Guid id, SaveGlobalTicketTemplateInput input);

    /// <summary>Also removes the space templates that reuse it; tickets made from them keep their text.</summary>
    Task DeleteAsync(Guid id);
}
