using Microsoft.AspNetCore.Authorization;
using SprintBoot.Identity;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace SprintBoot.Projects;

[Authorize(SprintBootPermissions.ManageUsers)]
public class ProjectAdminAppService(
    IRepository<Project, Guid> projects,
    IRepository<ProjectMember, Guid> members)
    : ApplicationService, IProjectAdminAppService
{
    public async Task<List<UserSpaceDto>> GetUserSpacesAsync(Guid userId)
    {
        var memberships = await members.GetListAsync(member => member.UserId == userId);
        var ids = memberships.Select(member => member.ProjectId).ToList();
        var spaces = (await projects.GetListAsync(project => ids.Contains(project.Id))).ToDictionary(project => project.Id);

        return [.. memberships
            .Where(member => spaces.ContainsKey(member.ProjectId))
            .OrderBy(member => spaces[member.ProjectId].Name)
            .Select(member => new UserSpaceDto
            {
                ProjectId = member.ProjectId,
                Key = spaces[member.ProjectId].Key,
                Name = spaces[member.ProjectId].Name,
                Role = member.Role,
                AddedAt = member.CreationTime,
            })];
    }
}
