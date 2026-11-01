using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;
using SprintBoot.Projects;

namespace SprintBoot.Agile;

public class Board : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = null!;

    public BoardType Type { get; private set; }

    public string? FilterConfig { get; private set; }

    /// <summary>Comma separated keys of <see cref="BoardCardFields"/>. Null means the default set.</summary>
    public string? CardFields { get; set; }

    protected Board()
    {
    }

    public Board(Guid id, Guid projectId, string name, BoardType type)
        : base(id)
    {
        ProjectId = projectId;
        Name = name;
        Type = type;
    }
}

public class BoardColumn : Entity<Guid>
{
    public Guid BoardId { get; private set; }

    public string Name { get; private set; } = null!;

    public int Order { get; private set; }

    public int? MinLimit { get; private set; }

    public int? MaxLimit { get; private set; }

    public bool IsHidden { get; private set; }

    protected BoardColumn()
    {
    }

    public BoardColumn(Guid id, Guid boardId, string name, int order, int? minLimit, int? maxLimit, bool isHidden = false)
        : base(id)
    {
        BoardId = boardId;
        Name = name;
        Order = order;
        MinLimit = minLimit;
        MaxLimit = maxLimit;
        IsHidden = isHidden;
    }
}

public class BoardColumnStatus : Entity<Guid>
{
    public Guid ColumnId { get; private set; }

    public Guid StatusId { get; private set; }

    protected BoardColumnStatus()
    {
    }

    public BoardColumnStatus(Guid id, Guid columnId, Guid statusId)
        : base(id)
    {
        ColumnId = columnId;
        StatusId = statusId;
    }
}

/// <summary>Kanban issues pulled from the backlog onto the board.</summary>
public class BoardEntry : Entity<Guid>
{
    public Guid BoardId { get; private set; }

    public Guid IssueId { get; private set; }

    protected BoardEntry()
    {
    }

    public BoardEntry(Guid id, Guid boardId, Guid issueId)
        : base(id)
    {
        BoardId = boardId;
        IssueId = issueId;
    }
}

public class Sprint : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid BoardId { get; private set; }

    public string Name { get; set; } = null!;

    public string? Goal { get; set; }

    public SprintState State { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public DateTime? CompletedAt { get; set; }

    protected Sprint()
    {
    }

    public Sprint(Guid id, Guid boardId, string name)
        : base(id)
    {
        BoardId = boardId;
        Name = name;
    }
}

public enum SprintOutcome
{
    Open = 0,
    Completed = 1,
    NotCompleted = 2,
}

/// <summary>History of an issue in sprints. Never deleted, so burndown can be added later.</summary>
public class SprintIssue : Entity<Guid>
{
    public Guid SprintId { get; private set; }

    public Guid IssueId { get; private set; }

    public DateTime AddedAt { get; private set; }

    public DateTime? RemovedAt { get; set; }

    public SprintOutcome Outcome { get; set; }

    protected SprintIssue()
    {
    }

    public SprintIssue(Guid id, Guid sprintId, Guid issueId, DateTime addedAt)
        : base(id)
    {
        SprintId = sprintId;
        IssueId = issueId;
        AddedAt = addedAt;
    }
}
