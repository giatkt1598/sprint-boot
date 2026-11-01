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

public interface IStatusLookup
{
    /// <summary>The status every new issue starts in until workflows exist.</summary>
    Task<StatusDto> GetInitialAsync();

    Task<List<StatusDto>> GetListAsync();
}
