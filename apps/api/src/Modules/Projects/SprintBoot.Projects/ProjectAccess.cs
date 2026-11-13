using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Users;

namespace SprintBoot.Projects;

public class ProjectAccess(
    ICurrentUser currentUser,
    IPermissionChecker permissionChecker,
    IRepository<Project, Guid> projects,
    IRepository<ProjectMember, Guid> members,
    IRepository<ProjectRole, Guid> roles,
    IRepository<ProjectRolePermission, Guid> rolePermissions)
    : IProjectAccess, ITransientDependency
{
    public async Task<bool> IsGrantedAsync(Guid projectId, string permission) =>
        (await GetPermissionsAsync(projectId)).Contains(permission);

    public Task CheckAsync(Guid projectId, string permission) => CheckAsync(projectId, permission, ignoreArchive: false);

    /// <summary>
    /// <paramref name="ignoreArchive"/> is for archiving itself: managing the archive must work on an archived space.
    /// </summary>
    public async Task CheckAsync(Guid projectId, string permission, bool ignoreArchive)
    {
        var project = await projects.FindAsync(projectId);
        var granted = project is null ? [] : await GetGrantedAsync(projectId);
        if (!granted.Contains(ProjectPermissions.ViewTickets))
        {
            // Outsiders must not learn that the project exists.
            throw new EntityNotFoundException(typeof(Project), projectId);
        }

        // Every write checks a permission other than viewing, so this one rule keeps an archived space read-only.
        if (project!.IsArchived && !ignoreArchive && permission != ProjectPermissions.ViewTickets)
        {
            throw new BusinessException(ProjectErrors.Archived).WithData("space", project.Key);
        }

        if (!granted.Contains(permission))
        {
            throw new AbpAuthorizationException($"Missing project permission {permission}");
        }
    }

    /// <summary>What the caller may do now: viewing only while the project is archived, nothing once it is deleted.</summary>
    public async Task<HashSet<string>> GetPermissionsAsync(Guid projectId)
    {
        var project = await projects.FindAsync(projectId);
        if (project is null)
        {
            return [];
        }

        var granted = await GetGrantedAsync(projectId);
        return project.IsArchived
            ? [.. granted.Where(key => key == ProjectPermissions.ViewTickets)]
            : granted;
    }

    /// <summary>The keys the caller's role holds, whatever state the project is in.</summary>
    public async Task<HashSet<string>> GetGrantedAsync(Guid projectId)
    {
        if (await IsSystemAdminAsync())
        {
            return [.. ProjectRoleNames.DefaultPermissions[ProjectRoleNames.Admin]];
        }

        if (currentUser.Id is not { } userId)
        {
            return [];
        }

        var member = await members.FirstOrDefaultAsync(item => item.ProjectId == projectId && item.UserId == userId);
        if (member is null)
        {
            return [];
        }

        var role = await roles.FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Name == member.Role);
        if (role is null)
        {
            return [];
        }

        var keys = await rolePermissions.GetListAsync(item => item.RoleId == role.Id);
        return [.. keys.Select(item => item.PermissionKey)];
    }

    public async Task<List<Guid>> GetVisibleProjectIdsAsync(bool includeArchived = true)
    {
        var query = await projects.GetQueryableAsync();
        if (!includeArchived)
        {
            query = query.Where(project => project.ArchivedAt == null);
        }

        if (!await IsSystemAdminAsync())
        {
            var userId = currentUser.GetId();
            var memberOf = (await members.GetQueryableAsync()).Where(item => item.UserId == userId).Select(item => item.ProjectId);
            query = query.Where(project => memberOf.Contains(project.Id));
        }

        return await query.Select(project => project.Id).ToListAsync();
    }

    public async Task CheckWritableAsync(Guid projectId)
    {
        var project = await projects.GetAsync(projectId);
        if (project.IsArchived)
        {
            throw new BusinessException(ProjectErrors.Archived).WithData("space", project.Key);
        }
    }

    public async Task<bool> IsArchivedAsync(Guid projectId) => (await projects.GetAsync(projectId)).IsArchived;

    public async Task<bool> IsActiveAsync(Guid projectId) => await projects.FindAsync(projectId) is { IsArchived: false };

    public async Task<string?> GetBranchTemplateAsync(Guid projectId) =>
        (await projects.GetAsync(projectId)).BranchNameTemplate;

    public async Task<int> GetSprintLengthDaysAsync(Guid projectId) =>
        (await projects.GetAsync(projectId)).SprintLengthDays;

    public async Task<ProjectRef> GetRefAsync(Guid projectId) => ToRef(await projects.GetAsync(projectId));

    public async Task<ProjectRef?> FindRefByKeyAsync(string key)
    {
        var project = await projects.FirstOrDefaultAsync(item => item.Key == key);
        return project is null ? null : ToRef(project);
    }

    public async Task<List<ProjectRef>> GetAllRefsAsync() => [.. (await projects.GetListAsync()).Select(ToRef)];

    public async Task<Dictionary<Guid, ProjectRef>> GetRefsAsync(IEnumerable<Guid> projectIds)
    {
        var ids = projectIds.Distinct().ToList();
        var found = await projects.GetListAsync(item => ids.Contains(item.Id));
        return found.ToDictionary(project => project.Id, ToRef);
    }

    public Task<bool> IsMemberAsync(Guid projectId, Guid userId) =>
        members.AnyAsync(item => item.ProjectId == projectId && item.UserId == userId);

    public async Task<string?> GetCurrentRoleAsync(Guid projectId)
    {
        if (currentUser.Id is not { } userId)
        {
            return null;
        }

        var member = await members.FirstOrDefaultAsync(item => item.ProjectId == projectId && item.UserId == userId);
        return member?.Role;
    }

    public async Task<Dictionary<Guid, string>> GetMemberRolesAsync(Guid projectId)
    {
        var list = await members.GetListAsync(item => item.ProjectId == projectId);
        return list.ToDictionary(item => item.UserId, item => item.Role);
    }

    public async Task<Dictionary<Guid, DateTime>> GetMemberJoinTimesAsync(Guid projectId)
    {
        var list = await members.GetListAsync(item => item.ProjectId == projectId);
        return list.ToDictionary(item => item.UserId, item => item.CreationTime);
    }

    public async Task<int> NextTicketNumberAsync(Guid projectId)
    {
        // A single UPDATE takes the row lock until commit, so concurrent creates serialize here.
        var dbContext = await projects.GetDbContextAsync();
        var numbers = await dbContext.Database
            .SqlQuery<int>($"""
                UPDATE projects.projects SET "NextTicketNumber" = "NextTicketNumber" + 1
                WHERE "Id" = {projectId}
                RETURNING "NextTicketNumber" - 1 AS "Value"
                """)
            .ToListAsync();
        return numbers.Single();
    }

    private Task<bool> IsSystemAdminAsync() => permissionChecker.IsGrantedAsync(SprintBootPermissions.AccessAllProjects);

    private static ProjectRef ToRef(Project project) => new(project.Id, project.Key, project.Name, project.DefaultBoardType, project.IsArchived);
}
