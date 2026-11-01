using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace SprintBoot.Workflows;

public class Workflow : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>Null means global.</summary>
    public Guid? ProjectId { get; private set; }

    protected Workflow()
    {
    }

    public Workflow(Guid id, string name, string? description, Guid? projectId = null)
        : base(id)
    {
        ProjectId = projectId;
        Name = name;
        Description = description;
    }
}

public class WorkflowVersion : Entity<Guid>
{
    public Guid WorkflowId { get; private set; }

    public int VersionNumber { get; private set; }

    public WorkflowVersionState State { get; set; }

    public Guid InitialStatusId { get; set; }

    public string? LayoutJson { get; set; }

    protected WorkflowVersion()
    {
    }

    public WorkflowVersion(Guid id, Guid workflowId, int versionNumber, Guid initialStatusId, string? layoutJson)
        : base(id)
    {
        WorkflowId = workflowId;
        VersionNumber = versionNumber;
        InitialStatusId = initialStatusId;
        LayoutJson = layoutJson;
        State = WorkflowVersionState.Draft;
    }
}

public class WorkflowStep : Entity<Guid>
{
    public Guid VersionId { get; private set; }

    public Guid StatusId { get; private set; }

    protected WorkflowStep()
    {
    }

    public WorkflowStep(Guid id, Guid versionId, Guid statusId)
        : base(id)
    {
        VersionId = versionId;
        StatusId = statusId;
    }
}

public class Transition : Entity<Guid>
{
    public Guid VersionId { get; private set; }

    public string Name { get; private set; } = null!;

    public Guid? FromStatusId { get; private set; }

    public Guid ToStatusId { get; private set; }

    protected Transition()
    {
    }

    public Transition(Guid id, Guid versionId, string name, Guid? fromStatusId, Guid toStatusId)
        : base(id)
    {
        VersionId = versionId;
        Name = name;
        FromStatusId = fromStatusId;
        ToStatusId = toStatusId;
    }

    public void ReplaceStatus(Guid statusId, Guid replacementId)
    {
        FromStatusId = FromStatusId == statusId ? replacementId : FromStatusId;
        ToStatusId = ToStatusId == statusId ? replacementId : ToStatusId;
    }
}

public class TransitionRule : Entity<Guid>
{
    public Guid TransitionId { get; private set; }

    public RuleKind Kind { get; private set; }

    public string RuleKey { get; private set; } = null!;

    public int Order { get; private set; }

    public string? ConfigJson { get; private set; }

    protected TransitionRule()
    {
    }

    public TransitionRule(Guid id, Guid transitionId, RuleKind kind, string ruleKey, int order, string? configJson)
        : base(id)
    {
        TransitionId = transitionId;
        Kind = kind;
        RuleKey = ruleKey;
        Order = order;
        ConfigJson = configJson;
    }
}

public class ProjectWorkflowMapping : Entity<Guid>
{
    public Guid ProjectId { get; private set; }

    public Guid IssueTypeId { get; private set; }

    public Guid WorkflowId { get; set; }

    protected ProjectWorkflowMapping()
    {
    }

    public ProjectWorkflowMapping(Guid id, Guid projectId, Guid issueTypeId, Guid workflowId)
        : base(id)
    {
        ProjectId = projectId;
        IssueTypeId = issueTypeId;
        WorkflowId = workflowId;
    }
}

public enum MigrationState
{
    Pending = 0,
    Completed = 1,
}

/// <summary>A queued move of many issues to a new workflow version.</summary>
public class WorkflowMigration : Entity<Guid>
{
    public string ScopeJson { get; private set; } = null!;

    public Guid ToVersionId { get; private set; }

    public string MappingJson { get; private set; } = null!;

    public MigrationState State { get; set; }

    public int Processed { get; set; }

    protected WorkflowMigration()
    {
    }

    public WorkflowMigration(Guid id, string scopeJson, Guid toVersionId, string mappingJson)
        : base(id)
    {
        ScopeJson = scopeJson;
        ToVersionId = toVersionId;
        MappingJson = mappingJson;
    }
}

public enum WebhookDeliveryState
{
    Pending = 0,
    Delivered = 1,
    Failed = 2,
}

/// <summary>Outgoing webhook, stored in the transition's transaction and sent by a worker.</summary>
public class WebhookDelivery : Entity<Guid>
{
    public string Url { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public WebhookDeliveryState State { get; set; }

    public int Attempts { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    protected WebhookDelivery()
    {
    }

    public WebhookDelivery(Guid id, string url, string payload, DateTime nextAttemptAt)
        : base(id)
    {
        Url = url;
        Payload = payload;
        NextAttemptAt = nextAttemptAt;
    }
}
