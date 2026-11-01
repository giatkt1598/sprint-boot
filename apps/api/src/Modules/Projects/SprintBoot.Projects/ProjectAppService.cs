using Microsoft.AspNetCore.Authorization;
using SprintBoot.Identity;
using SprintBoot.Issues;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Local;
using Volo.Abp.Users;

namespace SprintBoot.Projects;

[Authorize]
public class ProjectAppService(
    IRepository<Project, Guid> projects,
    IRepository<ProjectMember, Guid> members,
    IRepository<ProjectRole, Guid> roles,
    IRepository<ProjectRolePermission, Guid> rolePermissions,
    ProjectAccess access,
    IUserDirectory userDirectory,
    IIssueProjectStats issueStats,
    ILocalEventBus localEventBus)
    : ApplicationService, IProjectAppService
{
    public async Task<List<ProjectDto>> GetListAsync()
    {
        var visible = await access.GetVisibleProjectIdsAsync();
        var list = visible is null
            ? await projects.GetListAsync()
            : await projects.GetListAsync(project => visible.Contains(project.Id));
        return await ToDtosAsync(list.OrderBy(project => project.Name));
    }

    public async Task<ProjectDto> GetAsync(Guid id)
    {
        await access.CheckAsync(id, ProjectPermissions.ViewIssues);
        return (await ToDtosAsync([await projects.GetAsync(id)])).Single();
    }

    public async Task<ProjectDto> GetByKeyAsync(string key)
    {
        var project = await projects.FirstOrDefaultAsync(item => item.Key == key)
            ?? throw new EntityNotFoundException(typeof(Project), key);
        return await GetAsync(project.Id);
    }

    public async Task<ProjectDto> CreateAsync(CreateProjectInput input)
    {
        if (await projects.AnyAsync(project => project.Key == input.Key))
        {
            throw new BusinessException(ProjectErrors.KeyTaken).WithData("key", input.Key);
        }

        var creatorId = CurrentUser.GetId();
        var project = new Project(GuidGenerator.Create(), input.Key, input.Name, input.Type, input.LeadId ?? creatorId);
        await projects.InsertAsync(project, autoSave: true);

        foreach (var (name, keys) in ProjectRoleNames.DefaultPermissions)
        {
            var role = await roles.InsertAsync(new ProjectRole(GuidGenerator.Create(), project.Id, name));
            await rolePermissions.InsertManyAsync(
                keys.Select(key => new ProjectRolePermission(GuidGenerator.Create(), role.Id, key)));
        }

        await members.InsertAsync(
            new ProjectMember(GuidGenerator.Create(), project.Id, creatorId, ProjectRoleNames.Admin), autoSave: true);

        if (project.LeadId != creatorId)
        {
            await members.InsertAsync(
                new ProjectMember(GuidGenerator.Create(), project.Id, project.LeadId!.Value, ProjectRoleNames.Member), autoSave: true);
        }

        await localEventBus.PublishAsync(new ProjectCreatedEvent(project.Id));
        return (await ToDtosAsync([project])).Single();
    }

    public async Task<ProjectDto> UpdateAsync(Guid id, UpdateProjectInput input)
    {
        await access.CheckAsync(id, ProjectPermissions.Manage);
        var project = await projects.GetAsync(id);
        project.Name = input.Name;
        project.LeadId = input.LeadId;
        project.BranchNameTemplate = input.BranchNameTemplate;
        await projects.UpdateAsync(project, autoSave: true);
        return (await ToDtosAsync([project])).Single();
    }

    public async Task<List<ProjectMemberDto>> GetMembersAsync(Guid id)
    {
        await access.CheckAsync(id, ProjectPermissions.ViewIssues);
        var list = await members.GetListAsync(member => member.ProjectId == id);
        var users = await userDirectory.GetByIdsAsync(list.Select(member => member.UserId));
        return [.. list
            .Where(member => users.ContainsKey(member.UserId))
            .Select(member => new ProjectMemberDto { User = users[member.UserId], Role = member.Role })
            .OrderBy(member => member.User.Name)];
    }

    public async Task<ProjectMemberDto> AddMemberAsync(Guid id, AddMemberInput input)
    {
        await access.CheckAsync(id, ProjectPermissions.ManageMembers);
        EnsureValidRole(input.Role);

        if (await members.AnyAsync(member => member.ProjectId == id && member.UserId == input.UserId))
        {
            throw new BusinessException(ProjectErrors.AlreadyMember);
        }

        var users = await userDirectory.GetByIdsAsync([input.UserId]);
        if (!users.TryGetValue(input.UserId, out var user))
        {
            throw new EntityNotFoundException(typeof(ProjectMember), input.UserId);
        }

        await members.InsertAsync(new ProjectMember(GuidGenerator.Create(), id, input.UserId, input.Role));
        return new ProjectMemberDto { User = user, Role = input.Role };
    }

    public async Task<ProjectMemberDto> UpdateMemberAsync(Guid id, Guid userId, UpdateMemberInput input)
    {
        await access.CheckAsync(id, ProjectPermissions.ManageMembers);
        EnsureValidRole(input.Role);

        var member = await members.GetAsync(item => item.ProjectId == id && item.UserId == userId);
        if (member.Role == ProjectRoleNames.Admin && input.Role != ProjectRoleNames.Admin)
        {
            await EnsureAnotherAdminExistsAsync(id, userId);
        }

        member.Role = input.Role;
        await members.UpdateAsync(member);
        var users = await userDirectory.GetByIdsAsync([userId]);
        return new ProjectMemberDto { User = users[userId], Role = member.Role };
    }

    public async Task RemoveMemberAsync(Guid id, Guid userId)
    {
        await access.CheckAsync(id, ProjectPermissions.ManageMembers);
        var member = await members.GetAsync(item => item.ProjectId == id && item.UserId == userId);
        if (member.Role == ProjectRoleNames.Admin)
        {
            await EnsureAnotherAdminExistsAsync(id, userId);
        }

        await members.DeleteAsync(member);
    }

    private async Task EnsureAnotherAdminExistsAsync(Guid projectId, Guid userId)
    {
        var others = await members.AnyAsync(member =>
            member.ProjectId == projectId && member.UserId != userId && member.Role == ProjectRoleNames.Admin);
        if (!others)
        {
            throw new BusinessException(ProjectErrors.LastAdmin);
        }
    }

    private static void EnsureValidRole(string role)
    {
        if (!ProjectRoleNames.All.Contains(role))
        {
            throw new BusinessException(ProjectErrors.InvalidRole);
        }
    }

    private async Task<List<ProjectDto>> ToDtosAsync(IEnumerable<Project> list)
    {
        var items = list.ToList();
        var leads = await userDirectory.GetByIdsAsync(items.Where(p => p.LeadId.HasValue).Select(p => p.LeadId!.Value));
        var stats = await issueStats.GetAsync(items.Select(project => project.Id));

        var result = new List<ProjectDto>();
        foreach (var project in items)
        {
            var stat = stats.GetValueOrDefault(project.Id);
            result.Add(new ProjectDto
            {
                Id = project.Id,
                Key = project.Key,
                Name = project.Name,
                Type = project.DefaultBoardType,
                BranchNameTemplate = project.BranchNameTemplate,
                Lead = project.LeadId is { } leadId ? leads.GetValueOrDefault(leadId) : null,
                IssueCount = stat?.Count ?? 0,
                UpdatedAt = stat?.UpdatedAt ?? project.LastModificationTime ?? project.CreationTime,
                Permissions = [.. await access.GetPermissionsAsync(project.Id)],
            });
        }

        return result;
    }
}
