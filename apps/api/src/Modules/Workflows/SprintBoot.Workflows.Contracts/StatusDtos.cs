using System.ComponentModel.DataAnnotations;

namespace SprintBoot.Workflows;

public enum StatusCategory
{
    ToDo = 0,
    InProgress = 1,
    Done = 2,
}

public class StatusDto
{
    public required Guid Id { get; set; }

    public required string Name { get; set; }

    public required StatusCategory Category { get; set; }
}

public class StatusAdminDto : StatusDto
{
    public required int WorkflowCount { get; set; }

    public required int IssueCount { get; set; }

    public required DateTime UpdatedAt { get; set; }
}

public class SaveStatusInput
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public required string Name { get; set; }

    public StatusCategory Category { get; set; }
}

public interface IStatusLookup
{
    Task<List<StatusDto>> GetListAsync();
}
