using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace SprintBoot.Notifications;

[Authorize]
public class NotificationAppService(
    IRepository<Notification, Guid> notifications,
    IRepository<NotificationPreference, Guid> preferences,
    IClock clock)
    : ApplicationService, INotificationAppService
{
    public async Task<PagedResultDto<NotificationDto>> GetListAsync(NotificationListInput input)
    {
        var userId = CurrentUser.GetId();
        var query = (await notifications.GetQueryableAsync())
            .Where(row => row.UserId == userId && (!input.UnreadOnly || row.ReadAt == null));
        var total = await query.LongCountAsync();
        var page = await query.OrderByDescending(row => row.CreatedAt).Skip(input.SkipCount).Take(input.MaxResultCount).ToListAsync();
        return new PagedResultDto<NotificationDto>(total, [.. page.Select(ToDto)]);
    }

    public async Task<int> GetUnreadCountAsync()
    {
        var userId = CurrentUser.GetId();
        return await notifications.CountAsync(row => row.UserId == userId && row.ReadAt == null);
    }

    public async Task MarkReadAsync(Guid id)
    {
        var userId = CurrentUser.GetId();
        var row = await notifications.FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId)
            ?? throw new EntityNotFoundException(typeof(Notification), id);
        row.ReadAt ??= clock.Now;
        await notifications.UpdateAsync(row);
    }

    public async Task MarkAllReadAsync()
    {
        var userId = CurrentUser.GetId();
        var unread = await notifications.GetListAsync(row => row.UserId == userId && row.ReadAt == null);
        foreach (var row in unread)
        {
            row.ReadAt = clock.Now;
        }

        await notifications.UpdateManyAsync(unread);
    }

    public async Task<List<NotificationPreferenceDto>> GetPreferencesAsync()
    {
        var userId = CurrentUser.GetId();
        var saved = await preferences.GetListAsync(pref => pref.UserId == userId);
        return [.. NotificationEventTypes.All.Select(type => new NotificationPreferenceDto
        {
            EventType = type,
            Enabled = saved.FirstOrDefault(pref => pref.EventType == type)?.Enabled ?? true,
        })];
    }

    public async Task SavePreferencesAsync(SavePreferencesInput input)
    {
        var userId = CurrentUser.GetId();
        var saved = await preferences.GetListAsync(pref => pref.UserId == userId);
        foreach (var item in input.Items)
        {
            if (!NotificationEventTypes.All.Contains(item.EventType))
            {
                throw new BusinessException(NotificationErrors.UnknownEventType);
            }

            var row = saved.FirstOrDefault(pref => pref.EventType == item.EventType);
            if (row is null)
            {
                await preferences.InsertAsync(new NotificationPreference(GuidGenerator.Create(), userId, item.EventType, item.Enabled));
            }
            else
            {
                row.Enabled = item.Enabled;
                await preferences.UpdateAsync(row);
            }
        }
    }

    private static NotificationDto ToDto(Notification row) => new()
    {
        Id = row.Id,
        EventType = row.EventType,
        IssueKey = row.IssueKey,
        Title = row.Title,
        ActorName = row.ActorName,
        Detail = row.Detail,
        MergedCount = row.MergedCount,
        ReadAt = row.ReadAt,
        CreatedAt = row.CreatedAt,
    };
}

[Authorize]
public class NotificationSchemeAppService(
    IRepository<NotificationScheme, Guid> schemes,
    IRepository<NotificationRule, Guid> rules,
    IRepository<ProjectNotificationScheme, Guid> assignments,
    SchemeResolver resolver,
    IProjectAccess projectAccess)
    : ApplicationService, INotificationSchemeAppService
{
    [Authorize(SprintBootPermissions.ManageNotificationSchemes)]
    public async Task<List<NotificationSchemeDto>> GetListAsync()
    {
        var result = new List<NotificationSchemeDto>();
        foreach (var scheme in (await schemes.GetListAsync()).OrderByDescending(item => item.IsDefault).ThenBy(item => item.Name))
        {
            result.Add(await ToDtoAsync(scheme));
        }

        return result;
    }

    [Authorize(SprintBootPermissions.ManageNotificationSchemes)]
    public async Task<NotificationSchemeDto> GetAsync(Guid id) => await ToDtoAsync(await schemes.GetAsync(id));

    [Authorize(SprintBootPermissions.ManageNotificationSchemes)]
    public async Task<NotificationSchemeDto> CreateAsync(SaveSchemeInput input)
    {
        EnsureEvents(input);
        var scheme = await schemes.InsertAsync(new NotificationScheme(GuidGenerator.Create(), input.Name, input.Description, false), autoSave: true);
        await ReplaceRulesAsync(scheme.Id, input.Rules);
        return await ToDtoAsync(scheme);
    }

    [Authorize(SprintBootPermissions.ManageNotificationSchemes)]
    public async Task<NotificationSchemeDto> UpdateAsync(Guid id, SaveSchemeInput input)
    {
        EnsureEvents(input);
        var scheme = await schemes.GetAsync(id);
        scheme.Name = input.Name;
        scheme.Description = input.Description;
        await schemes.UpdateAsync(scheme, autoSave: true);
        await ReplaceRulesAsync(id, input.Rules);
        return await ToDtoAsync(scheme);
    }

    [Authorize(SprintBootPermissions.ManageNotificationSchemes)]
    public async Task DeleteAsync(Guid id)
    {
        var scheme = await schemes.GetAsync(id);
        if (scheme.IsDefault)
        {
            throw new BusinessException(NotificationErrors.DefaultSchemeLocked);
        }

        if (await assignments.AnyAsync(item => item.SchemeId == id))
        {
            throw new BusinessException(NotificationErrors.SchemeInUse);
        }

        await rules.DeleteAsync(rule => rule.SchemeId == id);
        await schemes.DeleteAsync(scheme);
    }

    public async Task<ProjectSchemeDto> GetForProjectAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewIssues);
        var scheme = await resolver.GetForProjectAsync(projectId);
        return new ProjectSchemeDto { SchemeId = scheme.Id, Name = scheme.Name };
    }

    public async Task<ProjectSchemeDto> SetForProjectAsync(SetProjectSchemeInput input)
    {
        await projectAccess.CheckAsync(input.ProjectId, ProjectPermissions.Manage);
        var scheme = await schemes.GetAsync(input.SchemeId);
        var assignment = await assignments.FirstOrDefaultAsync(item => item.ProjectId == input.ProjectId);
        if (assignment is null)
        {
            await assignments.InsertAsync(new ProjectNotificationScheme(GuidGenerator.Create(), input.ProjectId, input.SchemeId));
        }
        else
        {
            assignment.SchemeId = input.SchemeId;
            await assignments.UpdateAsync(assignment);
        }

        return new ProjectSchemeDto { SchemeId = scheme.Id, Name = scheme.Name };
    }

    public async Task<List<ProjectSchemeDto>> GetChoicesAsync()
    {
        var list = await schemes.GetListAsync();
        return [.. list.OrderBy(item => item.Name).Select(item => new ProjectSchemeDto { SchemeId = item.Id, Name = item.Name })];
    }

    private static void EnsureEvents(SaveSchemeInput input)
    {
        if (input.Rules.Any(rule => !NotificationEventTypes.All.Contains(rule.EventType)))
        {
            throw new BusinessException(NotificationErrors.UnknownEventType);
        }
    }

    private async Task ReplaceRulesAsync(Guid schemeId, List<NotificationRuleDto> input)
    {
        await rules.DeleteAsync(rule => rule.SchemeId == schemeId, autoSave: true);
        await rules.InsertManyAsync(
            input.Select(rule => new NotificationRule(GuidGenerator.Create(), schemeId, rule.EventType, rule.RecipientType, rule.RecipientValue)),
            autoSave: true);
    }

    private async Task<NotificationSchemeDto> ToDtoAsync(NotificationScheme scheme)
    {
        var list = await rules.GetListAsync(rule => rule.SchemeId == scheme.Id);
        return new NotificationSchemeDto
        {
            Id = scheme.Id,
            Name = scheme.Name,
            Description = scheme.Description,
            IsDefault = scheme.IsDefault,
            SpaceCount = await assignments.CountAsync(item => item.SchemeId == scheme.Id),
            Rules = [.. list.Select(rule => new NotificationRuleDto
            {
                EventType = rule.EventType,
                RecipientType = rule.RecipientType,
                RecipientValue = rule.RecipientValue,
            })],
        };
    }
}

public class NotificationsDataSeedContributor(
    IRepository<NotificationScheme, Guid> schemes,
    IRepository<NotificationRule, Guid> rules,
    IGuidGenerator guids) : IDataSeedContributor, ITransientDependency
{
    public async Task SeedAsync(DataSeedContext context)
    {
        if (await schemes.AnyAsync(scheme => scheme.IsDefault))
        {
            return;
        }

        var scheme = await schemes.InsertAsync(new NotificationScheme(guids.Create(), "Default", "Used by every space until another is chosen", true), autoSave: true);

        (string Event, RecipientType Type, string? Value)[] defaults =
        [
            (NotificationEventTypes.IssueCreated, RecipientType.ProjectRole, ProjectRoleNames.Admin),
            (NotificationEventTypes.IssueAssigned, RecipientType.Assignee, null),
            (NotificationEventTypes.IssueUpdated, RecipientType.Assignee, null),
            (NotificationEventTypes.IssueUpdated, RecipientType.Watcher, null),
            (NotificationEventTypes.CommentAdded, RecipientType.Assignee, null),
            (NotificationEventTypes.CommentAdded, RecipientType.Reporter, null),
            (NotificationEventTypes.CommentAdded, RecipientType.Watcher, null),
            (NotificationEventTypes.IssueTransitioned, RecipientType.Assignee, null),
            (NotificationEventTypes.IssueTransitioned, RecipientType.Reporter, null),
            (NotificationEventTypes.IssueTransitioned, RecipientType.Watcher, null),
            (NotificationEventTypes.WorkflowNotification, RecipientType.Assignee, null),
            (NotificationEventTypes.WorkflowNotification, RecipientType.Reporter, null),
            (NotificationEventTypes.WorkflowNotification, RecipientType.Watcher, null),
            (NotificationEventTypes.SprintStarted, RecipientType.ProjectRole, ProjectRoleNames.Member),
            (NotificationEventTypes.SprintStarted, RecipientType.ProjectRole, ProjectRoleNames.Admin),
            (NotificationEventTypes.SprintCompleted, RecipientType.ProjectRole, ProjectRoleNames.Member),
            (NotificationEventTypes.SprintCompleted, RecipientType.ProjectRole, ProjectRoleNames.Admin),
        ];
        await rules.InsertManyAsync(defaults.Select(rule => new NotificationRule(guids.Create(), scheme.Id, rule.Event, rule.Type, rule.Value)));
    }
}

/// <summary>Deletes notifications past the retention period.</summary>
public class NotificationCleanupWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int HourMilliseconds = 60 * 60 * 1000;
    private readonly int _retentionDays;

    public NotificationCleanupWorker(AbpAsyncTimer timer, IServiceScopeFactory scopeFactory, IConfiguration configuration)
        : base(timer, scopeFactory)
    {
        timer.Period = configuration.GetValue("Notifications:CleanupPollMilliseconds", HourMilliseconds);
        _retentionDays = configuration.GetValue("Notifications:RetentionDays", 90);
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        await using var handle = await services.GetRequiredService<IAbpDistributedLock>().TryAcquireAsync(nameof(NotificationCleanupWorker));
        if (handle is null)
        {
            return;
        }

        var cutoff = services.GetRequiredService<IClock>().Now.AddDays(-_retentionDays);
        using var uow = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        await services.GetRequiredService<IRepository<Notification, Guid>>().DeleteDirectAsync(row => row.CreatedAt < cutoff);
        await uow.CompleteAsync();
    }
}
