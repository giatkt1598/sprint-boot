using Microsoft.EntityFrameworkCore;
using SprintBoot.Identity;
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

    public async Task CheckAsync(Guid projectId, string permission)
    {
        var granted = await GetPermissionsAsync(projectId);
        if (!granted.Contains(ProjectPermissions.ViewIssues))
        {
            // Outsiders must not learn that the project exists.
            throw new EntityNotFoundException(typeof(Project), projectId);
        }

        if (!granted.Contains(permission))
        {
            throw new AbpAuthorizationException($"Missing project permission {permission}");
        }
    }

    public async Task<HashSet<string>> GetPermissionsAsync(Guid projectId)
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

    public async Task<List<Guid>?> GetVisibleProjectIdsAsync()
    {
        if (await IsSystemAdminAsync())
        {
            return null;
        }

        var userId = currentUser.GetId();
        var memberships = await members.GetListAsync(item => item.UserId == userId);
        return [.. memberships.Select(item => item.ProjectId)];
    }

    public async Task<ProjectRef> GetRefAsync(Guid projectId) => ToRef(await projects.GetAsync(projectId));

    public async Task<ProjectRef?> FindRefByKeyAsync(string key)
    {
        var project = await projects.FirstOrDefaultAsync(item => item.Key == key);
        return project is null ? null : ToRef(project);
    }

    public async Task<Dictionary<Guid, ProjectRef>> GetRefsAsync(IEnumerable<Guid> projectIds)
    {
        var ids = projectIds.Distinct().ToList();
        var found = await projects.GetListAsync(item => ids.Contains(item.Id));
        return found.ToDictionary(project => project.Id, ToRef);
    }

    public Task<bool> IsMemberAsync(Guid projectId, Guid userId) =>
        members.AnyAsync(item => item.ProjectId == projectId && item.UserId == userId);

    public async Task<int> NextIssueNumberAsync(Guid projectId)
    {
        // A single UPDATE takes the row lock until commit, so concurrent creates serialize here.
        var dbContext = await projects.GetDbContextAsync();
        var numbers = await dbContext.Database
            .SqlQuery<int>($"""
                UPDATE projects.projects SET "NextIssueNumber" = "NextIssueNumber" + 1
                WHERE "Id" = {projectId}
                RETURNING "NextIssueNumber" - 1 AS "Value"
                """)
            .ToListAsync();
        return numbers.Single();
    }

    private Task<bool> IsSystemAdminAsync() => permissionChecker.IsGrantedAsync(SprintBootPermissions.AccessAllProjects);

    private static ProjectRef ToRef(Project project) => new(project.Id, project.Key, project.Name);
}
