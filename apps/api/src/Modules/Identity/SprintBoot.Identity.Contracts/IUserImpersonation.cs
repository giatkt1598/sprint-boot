using System.Security.Claims;

namespace SprintBoot.Identity;

/// <summary>
/// For trusted integrations acting for a person they name by email, such as Docmost creating a ticket for its user.
/// Run the work under <c>ICurrentPrincipalAccessor.Change(principal)</c> in the caller itself: a change made inside an
/// awaited method does not flow back out of it.
/// </summary>
public interface IUserImpersonation
{
    /// <summary>The person's claims, roles included, so every permission check is theirs; null when no active person has the email.</summary>
    Task<ClaimsPrincipal?> FindPrincipalAsync(string email);
}
