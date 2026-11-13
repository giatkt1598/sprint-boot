using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace SprintBoot.Projects;

public class ProjectSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context) =>
        context.Add(new SettingDefinition(
            ProjectSettings.TrashRetentionDays, ProjectSettings.DefaultTrashRetentionDays.ToString(), isVisibleToClients: false));
}

/// <summary>
/// Removes spaces whose time in the trash ran out. Only the Projects tables go here; every other module hears
/// <see cref="ProjectPurgedEto"/> and deletes its own rows, since no module touches another's tables.
/// </summary>
public class ProjectPurger(
    IRepository<Project, Guid> projects,
    IRepository<ProjectMember, Guid> members,
    IRepository<ProjectRole, Guid> roles,
    IRepository<ProjectRolePermission, Guid> rolePermissions,
    IDataFilter dataFilter,
    ISettingProvider settings,
    SpaceLogos logos,
    IDistributedEventBus eventBus,
    IUnitOfWorkManager unitOfWork,
    IClock clock,
    ILogger<ProjectPurger> logger) : ITransientDependency
{
    public static async Task<TimeSpan> RetentionAsync(ISettingProvider settings)
    {
        var days = int.TryParse(await settings.GetOrNullAsync(ProjectSettings.TrashRetentionDays), out var value) && value >= 0
            ? value
            : ProjectSettings.DefaultTrashRetentionDays;
        return TimeSpan.FromDays(days);
    }

    /// <summary>Purges every space deleted longer ago than the retention setting; returns how many went.</summary>
    public async Task<int> PurgeDueAsync()
    {
        var cutoff = clock.Now - await RetentionAsync(settings);
        List<Guid> due;
        using (var uow = unitOfWork.Begin(requiresNew: true))
        using (dataFilter.Disable<ISoftDelete>())
        {
            due = [.. (await projects.GetListAsync(project => project.IsDeleted && project.DeletionTime <= cutoff)).Select(project => project.Id)];
            await uow.CompleteAsync();
        }

        foreach (var id in due)
        {
            await PurgeAsync(id);
        }

        return due.Count;
    }

    /// <summary>Purges one space from the trash at once, whatever its age. A space outside the trash is left alone.</summary>
    public async Task PurgeAsync(Guid projectId)
    {
        using var uow = unitOfWork.Begin(requiresNew: true, isTransactional: true);
        using (dataFilter.Disable<ISoftDelete>())
        {
            var project = await projects.FindAsync(projectId);
            if (project is not { IsDeleted: true })
            {
                return;
            }

            var roleIds = (await roles.GetQueryableAsync()).Where(role => role.ProjectId == projectId).Select(role => role.Id);
            await rolePermissions.DeleteDirectAsync(permission => roleIds.Contains(permission.RoleId));
            await roles.DeleteDirectAsync(role => role.ProjectId == projectId);
            await members.DeleteDirectAsync(member => member.ProjectId == projectId);
            await logos.RemoveAsync(project);
            await projects.HardDeleteAsync(project, autoSave: true);

            // In the same transaction, so the other modules hear of it exactly when the key is free again.
            await eventBus.PublishAsync(new ProjectPurgedEto { ProjectId = project.Id, Key = project.Key }, useOutbox: true);
            logger.LogInformation("Purged space {Key} ({ProjectId}) from the trash", project.Key, project.Id);
        }

        await uow.CompleteAsync();
    }
}

public class ProjectPurgeWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int HourMilliseconds = 60 * 60 * 1000;

    public ProjectPurgeWorker(AbpAsyncTimer timer, IServiceScopeFactory scopeFactory)
        : base(timer, scopeFactory)
    {
        timer.Period = HourMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        await using var handle = await services.GetRequiredService<IAbpDistributedLock>().TryAcquireAsync(nameof(ProjectPurgeWorker));
        if (handle is not null)
        {
            await services.GetRequiredService<ProjectPurger>().PurgeDueAsync();
        }
    }
}
