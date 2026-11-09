using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace SprintBoot.Tickets;

[DependsOn(typeof(AbpDddApplicationContractsModule))]
public class TicketsContractsModule : AbpModule
{
}
