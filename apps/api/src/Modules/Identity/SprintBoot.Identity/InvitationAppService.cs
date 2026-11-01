using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Emailing;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace SprintBoot.Identity;

[Authorize]
public class InvitationAppService(
    IRepository<Invitation, Guid> invitations,
    IRepository<InvitationProject, Guid> invitationProjects,
    IdentityUserManager userManager,
    IProjectAccess projectAccess,
    IProjectMembership membership,
    IUserDirectory userDirectory,
    IDistributedEventBus eventBus,
    IStringEncryptionService encryption,
    IConfiguration configuration,
    IClock clock)
    : ApplicationService, IInvitationAppService
{
    private static readonly string[] RoleOrder = [ProjectRoleNames.Viewer, ProjectRoleNames.Member, ProjectRoleNames.Admin];

    public async Task<PagedResultDto<InvitationDto>> GetListAsync(InvitationListInput input)
    {
        var all = await CanManageAllAsync();
        var userId = CurrentUser.GetId();
        var list = await invitations.GetListAsync(item => all || item.InvitedById == userId);
        var now = clock.Now;
        var filtered = list
            .Where(item => input.Status is null || item.EffectiveStatus(now) == input.Status)
            .OrderByDescending(item => item.CreationTime)
            .ToList();
        var page = filtered.Skip(input.SkipCount).Take(input.MaxResultCount).ToList();

        var ids = page.Select(item => item.Id).ToList();
        var projects = await invitationProjects.GetListAsync(item => ids.Contains(item.InvitationId));
        var people = await userDirectory.GetByIdsAsync(page.Select(item => item.InvitedById));

        return new PagedResultDto<InvitationDto>(filtered.Count, [.. page.Select(item => ToDto(item, projects, people))]);
    }

    public async Task<InvitationLinkDto> CreateAsync(CreateInvitationInput input)
    {
        var email = InvitationToken.NormalizeEmail(input.Email);
        await AuthorizeAsync(input);

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (input.Projects.Count == 0)
            {
                throw new BusinessException(InvitationErrors.AlreadyRegistered);
            }

            foreach (var project in input.Projects)
            {
                await membership.AddAsync(project.ProjectId, existing.Id, project.Role, CurrentUser.GetId());
            }

            return new InvitationLinkDto { Invitation = null, Link = null, AddedDirectly = true };
        }

        await EnsureRateAsync();
        var token = InvitationToken.NewToken();
        var hash = InvitationToken.Hash(token);
        var expires = clock.Now.AddDays(configuration.GetValue("Invitations:ExpiryDays", 7));

        var pending = (await invitations.GetListAsync(item => item.Email == email && item.Status == InvitationStatus.Pending)).FirstOrDefault();
        if (pending is null)
        {
            pending = await invitations.InsertAsync(
                new Invitation(GuidGenerator.Create(), email, hash, input.SystemRole, CurrentUser.GetId(), expires), autoSave: true);
        }
        else
        {
            pending.TokenHash = hash;
            pending.SystemRole = input.SystemRole;
            pending.InvitedById = CurrentUser.GetId();
            pending.ExpiresAt = expires;
            pending.EmailStatus = InvitationEmailStatus.Queued;
            await invitations.UpdateAsync(pending, autoSave: true);
            await invitationProjects.DeleteAsync(item => item.InvitationId == pending.Id, autoSave: true);
        }

        await invitationProjects.InsertManyAsync(
            input.Projects.Select(project => new InvitationProject(GuidGenerator.Create(), pending.Id, project.ProjectId, project.Role)),
            autoSave: true);

        return await LinkAsync(pending, token);
    }

    public async Task<InvitationLinkDto> ResendAsync(Guid id)
    {
        var invitation = await GetOwnAsync(id);
        var token = InvitationToken.NewToken();
        invitation.TokenHash = InvitationToken.Hash(token);
        invitation.ExpiresAt = clock.Now.AddDays(configuration.GetValue("Invitations:ExpiryDays", 7));
        invitation.Status = InvitationStatus.Pending;
        invitation.EmailStatus = InvitationEmailStatus.Queued;
        await invitations.UpdateAsync(invitation, autoSave: true);
        return await LinkAsync(invitation, token);
    }

    public async Task RevokeAsync(Guid id)
    {
        var invitation = await GetOwnAsync(id);
        invitation.Status = InvitationStatus.Revoked;
        await invitations.UpdateAsync(invitation);
    }

    private async Task<InvitationLinkDto> LinkAsync(Invitation invitation, string token)
    {
        await eventBus.PublishAsync(
            new InvitationEmailEto { InvitationId = invitation.Id, EncryptedToken = encryption.Encrypt(token)! },
            useOutbox: true);

        var projects = await invitationProjects.GetListAsync(item => item.InvitationId == invitation.Id);
        var people = await userDirectory.GetByIdsAsync([invitation.InvitedById]);
        return new InvitationLinkDto
        {
            Invitation = ToDto(invitation, projects, people),
            Link = InvitationLinks.Build(configuration, token),
            AddedDirectly = false,
        };
    }

    private async Task<Invitation> GetOwnAsync(Guid id)
    {
        var invitation = await invitations.GetAsync(id);
        if (invitation.InvitedById != CurrentUser.GetId() && !await CanManageAllAsync())
        {
            throw new AbpAuthorizationException(code: InvitationErrors.Forbidden);
        }

        return invitation;
    }

    private async Task<bool> CanManageAllAsync() => await AuthorizationService.IsGrantedAsync(SprintBootPermissions.ManageInvitations);

    private async Task AuthorizeAsync(CreateInvitationInput input)
    {
        if (input.SystemRole is null && input.Projects.Count == 0)
        {
            throw new BusinessException(InvitationErrors.NothingToGrant);
        }

        var isAdmin = await AuthorizationService.IsGrantedAsync(SprintBootPermissions.ManageUsers);
        if (input.SystemRole is not null && !isAdmin)
        {
            throw new AbpAuthorizationException(code: InvitationErrors.Forbidden);
        }

        if (input.SystemRole is not (null or SystemRoles.Admin or SystemRoles.Member))
        {
            throw new BusinessException(InvitationErrors.NothingToGrant);
        }

        foreach (var project in input.Projects)
        {
            if (!RoleOrder.Contains(project.Role))
            {
                throw new BusinessException(ProjectErrors.InvalidRole);
            }

            await projectAccess.CheckAsync(project.ProjectId, ProjectPermissions.ManageMembers);
            var own = isAdmin ? ProjectRoleNames.Admin : await projectAccess.GetCurrentRoleAsync(project.ProjectId);
            if (Array.IndexOf(RoleOrder, project.Role) > Array.IndexOf(RoleOrder, own))
            {
                throw new BusinessException(InvitationErrors.RoleTooHigh);
            }
        }
    }

    private async Task EnsureRateAsync()
    {
        var since = clock.Now.AddHours(-1);
        var userId = CurrentUser.GetId();
        var sent = await invitations.CountAsync(item => item.InvitedById == userId && item.CreationTime >= since);
        if (sent >= configuration.GetValue("Invitations:MaxPerHour", 50))
        {
            throw new BusinessException(InvitationErrors.RateLimited);
        }
    }

    private InvitationDto ToDto(Invitation item, List<InvitationProject> projects, Dictionary<Guid, UserRefDto> people) => new()
    {
        Id = item.Id,
        Email = item.Email,
        Status = item.EffectiveStatus(clock.Now),
        EmailStatus = item.EmailStatus,
        SystemRole = item.SystemRole,
        InvitedBy = people.GetValueOrDefault(item.InvitedById),
        ExpiresAt = item.ExpiresAt,
        CreationTime = item.CreationTime,
        Projects = [.. projects.Where(project => project.InvitationId == item.Id)
            .Select(project => new InvitationProjectInput { ProjectId = project.ProjectId, Role = project.ProjectRole })],
    };
}

public static class InvitationLinks
{
    public static string Build(IConfiguration configuration, string token) =>
        $"{SpaUrls.GetBaseUrl(configuration)}/accept-invitation?token={WebUtility.UrlEncode(token)}";
}

/// <summary>A delivery failure stays visible on the invitation; it never loses the invitation.</summary>
public class InvitationEmailHandler(
    IRepository<Invitation, Guid> invitations,
    IEmailSender emailSender,
    IStringEncryptionService encryption,
    IConfiguration configuration,
    ILogger<InvitationEmailHandler> logger)
    : IDistributedEventHandler<InvitationEmailEto>, ITransientDependency
{
    private const int Attempts = 3;

    [UnitOfWork]
    public virtual async Task HandleEventAsync(InvitationEmailEto eventData)
    {
        var invitation = await invitations.FindAsync(eventData.InvitationId);
        if (invitation is null)
        {
            return;
        }

        var link = InvitationLinks.Build(configuration, encryption.Decrypt(eventData.EncryptedToken)!);
        var texts = EmailTexts.Load("en");
        var body = $"<p>{WebUtility.HtmlEncode(texts["inviteBody"])}</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(texts["inviteAction"])}</a></p>";

        invitation.EmailStatus = InvitationEmailStatus.Failed;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                await emailSender.SendAsync(invitation.Email, texts["inviteSubject"], body);
                invitation.EmailStatus = InvitationEmailStatus.Sent;
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The link is never logged, only the invitation id.
                logger.LogWarning(exception, "Invitation {InvitationId} email failed on attempt {Attempt}", invitation.Id, attempt);
            }
        }

        await invitations.UpdateAsync(invitation);
    }
}
