using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace SprintBoot.Workflows;

public class Status : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Name { get; set; } = null!;

    public StatusCategory Category { get; set; }

    public int Order { get; private set; }

    protected Status()
    {
    }

    public Status(Guid id, string name, StatusCategory category, int order)
        : base(id)
    {
        Name = name;
        Category = category;
        Order = order;
    }
}
