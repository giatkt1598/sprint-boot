using Microsoft.AspNetCore.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Timing;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace SprintBoot.Identity;

/// <param name="PictureUrl">Only set for providers whose picture addresses we trust to fetch.</param>
public record ExternalIdentity(
    string Provider, string ProviderDisplayName, string Subject, string? Email, bool EmailVerified, string? Name, string? PictureUrl = null);

public static class InvitationToken
{
    public static string Hash(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static string NewToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}

/// <summary>Finds usable invitations and applies what they grant.</summary>
public class InvitationAcceptor(
    IRepository<Invitation, Guid> invitations,
    IRepository<InvitationProject, Guid> invitationProjects,
    IdentityUserManager userManager,
    IProjectMembership membership,
    IClock clock) : ITransientDependency
{
    public async Task<Invitation> GetUsableAsync(string token)
    {
        var hash = InvitationToken.Hash(token);
        var invitation = await invitations.FirstOrDefaultAsync(item => item.TokenHash == hash);
        // The same answer for unknown, used, revoked and expired links.
        return invitation is not null && invitation.IsUsable(clock.Now)
            ? invitation
            : throw new BusinessException(InvitationErrors.Invalid);
    }

    /// <summary>The newest invitation to this address that can still be accepted.</summary>
    public async Task<Invitation?> FindUsableAsync(string email)
    {
        var normalized = InvitationToken.NormalizeEmail(email);
        var now = clock.Now;
        return (await invitations.GetListAsync(item => item.Email == normalized && item.Status == InvitationStatus.Pending))
            .Where(item => item.IsUsable(now))
            .OrderByDescending(item => item.CreationTime)
            .FirstOrDefault();
    }

    public async Task ApplyAsync(Invitation invitation, IdentityUser user)
    {
        // A project invitation carries no system role, yet a new person still needs the basic one.
        var role = invitation.SystemRole ?? SystemRoles.Member;
        if (invitation.SystemRole is not null || (await userManager.GetRolesAsync(user)).Count == 0)
        {
            if (!await userManager.IsInRoleAsync(user, role))
            {
                (await userManager.AddToRoleAsync(user, role)).CheckErrors();
            }
        }

        foreach (var project in await invitationProjects.GetListAsync(item => item.InvitationId == invitation.Id))
        {
            await membership.AddAsync(project.ProjectId, user.Id, project.ProjectRole, invitation.InvitedById);
        }

        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedAt = clock.Now;
        await invitations.UpdateAsync(invitation, autoSave: true);
    }
}

/// <summary>
/// Decides what an external sign-in may do. Every path that attaches an external
/// identity to an account requires an email the IdP has verified.
/// </summary>
public class ExternalAccountLinker(
    IdentityUserManager userManager,
    IRepository<AuthPolicy, Guid> policies,
    InvitationAcceptor invitations,
    AvatarImporter avatars,
    IGuidGenerator guids) : ITransientDependency
{
    public async Task<IdentityUser> ResolveAsync(ExternalIdentity identity, string? invitationToken)
    {
        // Keyed by the IdP subject, so an email change at the IdP keeps the same account.
        var known = await userManager.FindByLoginAsync(identity.Provider, identity.Subject);
        if (known is not null)
        {
            return EnsureActive(known);
        }

        if (string.IsNullOrWhiteSpace(identity.Email))
        {
            throw new BusinessException(SsoErrors.EmailMissing);
        }

        var email = InvitationToken.NormalizeEmail(identity.Email);
        if (invitationToken is not null)
        {
            return await AcceptAsync(identity, email, invitationToken);
        }

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (!identity.EmailVerified)
            {
                throw new BusinessException(SsoErrors.EmailNotVerified);
            }

            EnsureActive(existing);
            await AddLoginAsync(existing, identity);
            await FillProfileAsync(existing, identity);
            return existing;
        }

        // Someone invited may sign in with the IdP straight from the login page instead of the
        // link; the verified address proves it is them, as the link would have.
        if (identity.EmailVerified && await invitations.FindUsableAsync(email) is { } invited)
        {
            var created = await CreateAsync(identity, email, role: null);
            await invitations.ApplyAsync(invited, created);
            return created;
        }

        var policy = await policies.FirstOrDefaultAsync();
        if (policy is not { SelfRegistrationEnabled: true })
        {
            throw new BusinessException(SsoErrors.RegistrationClosed);
        }

        if (!identity.EmailVerified)
        {
            throw new BusinessException(SsoErrors.EmailNotVerified);
        }

        if (!DomainAllowed(email, policy.AllowedDomains))
        {
            throw new BusinessException(SsoErrors.DomainNotAllowed);
        }

        return await CreateAsync(identity, email, SystemRoles.Member);
    }

    public static bool DomainAllowed(string email, string? allowedDomains)
    {
        var domains = (allowedDomains ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var domain = email[(email.LastIndexOf('@') + 1)..];
        return domains.Length == 0 || domains.Any(item => item.Equals(domain, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IdentityUser> AcceptAsync(ExternalIdentity identity, string email, string token)
    {
        var invitation = await invitations.GetUsableAsync(token);
        if (!identity.EmailVerified || InvitationToken.NormalizeEmail(invitation.Email) != email)
        {
            throw new BusinessException(InvitationErrors.EmailMismatch);
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = await CreateAsync(identity, email, role: null);
        }
        else
        {
            await AddLoginAsync(EnsureActive(user), identity);
            await FillProfileAsync(user, identity);
        }

        await invitations.ApplyAsync(invitation, user);
        return user;
    }

    private async Task<IdentityUser> CreateAsync(ExternalIdentity identity, string email, string? role)
    {
        var user = new IdentityUser(guids.Create(), email, email) { Name = identity.Name ?? email };
        user.SetEmailConfirmed(true);
        (await userManager.CreateAsync(user)).CheckErrors();
        if (role is not null)
        {
            (await userManager.AddToRoleAsync(user, role)).CheckErrors();
        }

        await AddLoginAsync(user, identity);
        await FillProfileAsync(user, identity);
        return user;
    }

    /// <summary>
    /// Takes the name and picture from the IdP for whatever the profile still lacks, so people
    /// start with a recognisable face; anything they set themselves is never overwritten.
    /// </summary>
    private async Task FillProfileAsync(IdentityUser user, ExternalIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.Name) && (string.IsNullOrWhiteSpace(user.Name) || user.Name == user.Email))
        {
            user.Name = identity.Name;
            (await userManager.UpdateAsync(user)).CheckErrors();
        }

        if (identity.PictureUrl is not null && UserAvatars.Url(user) is null)
        {
            await avatars.TryImportAsync(user, identity.PictureUrl);
        }
    }

    private async Task AddLoginAsync(IdentityUser user, ExternalIdentity identity) =>
        (await userManager.AddLoginAsync(user, new UserLoginInfo(identity.Provider, identity.Subject, identity.ProviderDisplayName))).CheckErrors();

    private static IdentityUser EnsureActive(IdentityUser user) =>
        user.IsActive && (user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow)
            ? user
            : throw new BusinessException(SsoErrors.AccountDisabled);
}
