using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace SprintBoot.Issues;

/// <summary>Repeated inserts at one spot lengthen ranks; this restores short, evenly spaced ones.</summary>
public class RankRebalanceWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int HourMilliseconds = 60 * 60 * 1000;

    public RankRebalanceWorker(AbpAsyncTimer timer, IServiceScopeFactory scopeFactory)
        : base(timer, scopeFactory)
    {
        timer.Period = HourMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        await using var handle = await services.GetRequiredService<IAbpDistributedLock>().TryAcquireAsync(nameof(RankRebalanceWorker));
        if (handle is not null)
        {
            await RebalanceAsync(services);
        }
    }

    public static async Task RebalanceAsync(IServiceProvider services)
    {
        var issues = services.GetRequiredService<IRepository<Issue, Guid>>();
        var unitOfWork = services.GetRequiredService<IUnitOfWorkManager>();

        using var uow = unitOfWork.Begin(requiresNew: true, isTransactional: true);
        var query = await issues.GetQueryableAsync();
        var projects = await query.Where(issue => issue.Rank.Length > RankMath.RebalanceLength)
            .Select(issue => issue.ProjectId).Distinct().ToListAsync();

        foreach (var projectId in projects)
        {
            var rows = await query.Where(issue => issue.ProjectId == projectId)
                .Select(issue => new { issue.Id, issue.Rank, issue.Number }).ToListAsync();
            var ordered = rows.OrderBy(row => row.Rank, StringComparer.Ordinal).ThenBy(row => row.Number).ToList();
            var ranks = RankMath.Sequence(ordered.Count);
            for (var index = 0; index < ordered.Count; index++)
            {
                var (id, rank) = (ordered[index].Id, ranks[index]);
                await query.Where(issue => issue.Id == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(issue => issue.Rank, rank));
            }
        }

        await uow.CompleteAsync();
    }
}
