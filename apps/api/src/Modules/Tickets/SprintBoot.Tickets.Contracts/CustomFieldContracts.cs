using System.ComponentModel.DataAnnotations;
using SprintBoot.Identity;
using Volo.Abp.Application.Services;

namespace SprintBoot.Tickets;

/// <summary>What a custom field holds. The kind is fixed once the field exists.</summary>
public enum CustomFieldType
{
    /// <summary>One line of plain text.</summary>
    Text = 0,

    /// <summary>Several lines of plain text.</summary>
    Paragraph = 1,

    Number = 2,

    Date = 3,

    /// <summary>One option of a list.</summary>
    Select = 4,

    /// <summary>Any number of options of a list.</summary>
    MultiSelect = 5,

    /// <summary>One person.</summary>
    User = 6,

    Checkbox = 7,

    /// <summary>An http or https address.</summary>
    Url = 8,
}

public static class CustomFieldConsts
{
    public const int MaxNameLength = 64;
    public const int MaxKeyLength = 48;
    public const int MaxDescriptionLength = 500;
    public const int MaxOptionLength = 64;
    public const int MaxOptions = 100;
    public const int MaxTextLength = 2000;
    public const int MaxColorLength = 32;

    /// <summary>
    /// Field keys of the ticket API and of MCP: "cf." and the field's key, such as "cf.environment", so a custom field
    /// can never take the name of a built-in one.
    /// </summary>
    public const string KeyPrefix = "cf.";
}

public static class CustomFieldErrors
{
    public const string NameRequired = "SprintBoot:CustomFieldNameRequired";
    public const string OptionsRequired = "SprintBoot:CustomFieldOptionsRequired";
    public const string OptionInvalid = "SprintBoot:CustomFieldOptionInvalid";
    public const string TypeMismatch = "SprintBoot:CustomFieldTypeMismatch";
    public const string NotInSpace = "SprintBoot:CustomFieldNotInSpace";
    public const string InvalidValue = "SprintBoot:CustomFieldInvalidValue";
}

public class CustomFieldOptionDto
{
    public required Guid Id { get; set; }

    public required string Label { get; set; }

    /// <summary>A colour name of the theme: gray, blue, green, amber, red or violet. Null is gray.</summary>
    public string? Color { get; set; }

    /// <summary>No longer offered; tickets that have it keep it.</summary>
    public required bool IsHidden { get; set; }
}

/// <summary>A field people define for tickets, for the whole workspace or for one space.</summary>
public class CustomFieldDto
{
    public required Guid Id { get; set; }

    /// <summary>The space the field belongs to; null for a workspace field that spaces turn on.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Never changes, even when the name does; the API and MCP use it as "cf." and the key.</summary>
    public required string Key { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public required CustomFieldType Type { get; set; }

    /// <summary>The options of a select field, in order, hidden ones included.</summary>
    public required List<CustomFieldOptionDto> Options { get; set; }

    /// <summary>
    /// What a new ticket starts with, as the API takes the value: a JSON string, number, true or false, or an option
    /// id. Null for none.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>How many tickets have a value in it.</summary>
    public required int UsageCount { get; set; }
}

public class SaveCustomFieldOptionInput
{
    /// <summary>An option the field already has; null adds one.</summary>
    public Guid? Id { get; set; }

    [Required]
    [StringLength(CustomFieldConsts.MaxOptionLength)]
    public required string Label { get; set; }

    [StringLength(CustomFieldConsts.MaxColorLength)]
    public string? Color { get; set; }

    public bool IsHidden { get; set; }
}

public class CreateCustomFieldInput
{
    /// <summary>The space the field belongs to; null makes a workspace field.</summary>
    public Guid? ProjectId { get; set; }

    [Required]
    [StringLength(CustomFieldConsts.MaxNameLength)]
    public required string Name { get; set; }

    [StringLength(CustomFieldConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public required CustomFieldType Type { get; set; }

    /// <summary>Select fields only, in order.</summary>
    public List<SaveCustomFieldOptionInput>? Options { get; set; }

    public string? DefaultValue { get; set; }
}

/// <summary>Name, description, options and default; the kind and the key stay.</summary>
public class UpdateCustomFieldInput
{
    [Required]
    [StringLength(CustomFieldConsts.MaxNameLength)]
    public required string Name { get; set; }

    [StringLength(CustomFieldConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>Select fields only: every option, in order. Leaving out an option that tickets use hides it instead.</summary>
    public List<SaveCustomFieldOptionInput>? Options { get; set; }

    public string? DefaultValue { get; set; }
}

/// <summary>A field as one space uses it.</summary>
public class SpaceCustomFieldDto
{
    public required CustomFieldDto Field { get; set; }

    /// <summary>On in the space. A field of the space itself is always on.</summary>
    public required bool Enabled { get; set; }

    /// <summary>The ticket types that show it. Empty means none.</summary>
    public required Guid[] TicketTypeIds { get; set; }
}

public class SpaceCustomFieldInput
{
    public required Guid FieldId { get; set; }

    public required Guid[] TicketTypeIds { get; set; }
}

/// <summary>The fields a space uses, in the order tickets show them. Workspace fields left out are turned off.</summary>
public class SaveSpaceCustomFieldsInput
{
    [Required]
    public required List<SpaceCustomFieldInput> Items { get; set; }
}

/// <summary>One custom field of a ticket and its value.</summary>
public class TicketCustomFieldDto
{
    public required CustomFieldDto Field { get; set; }

    /// <summary>Text, Paragraph and Url.</summary>
    public string? Text { get; set; }

    public decimal? Number { get; set; }

    public DateOnly? Date { get; set; }

    public bool? Checked { get; set; }

    public UserRefDto? User { get; set; }

    /// <summary>The chosen options of a select field, in the field's order.</summary>
    public required List<CustomFieldOptionDto> Options { get; set; }
}

public interface ICustomFieldAppService : IApplicationService
{
    /// <summary>Workspace fields, for admins; or the fields of one space itself.</summary>
    Task<List<CustomFieldDto>> GetListAsync(Guid? projectId);

    Task<CustomFieldDto> CreateAsync(CreateCustomFieldInput input);

    Task<CustomFieldDto> UpdateAsync(Guid id, UpdateCustomFieldInput input);

    /// <summary>The field goes away from every ticket; the values stay stored.</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Every field the space can use, workspace ones included, in its order, on or off.</summary>
    Task<List<SpaceCustomFieldDto>> GetSpaceFieldsAsync(Guid projectId);

    Task<List<SpaceCustomFieldDto>> SetSpaceFieldsAsync(Guid projectId, SaveSpaceCustomFieldsInput input);
}
