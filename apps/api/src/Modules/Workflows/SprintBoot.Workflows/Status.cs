using Volo.Abp.Domain.Entities;

namespace SprintBoot.Workflows;

public class Status : AggregateRoot<Guid>
{
    public string Name { get; private set; } = null!;

    public StatusCategory Category { get; private set; }

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
