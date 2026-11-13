using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Services;

namespace SprintBoot.DocsIntegration;

public enum DocsConnectionStatus
{
    Connected = 0,
    NeedsAttention = 1,
}

public enum DocsProblem
{
    None = 0,

    /// <summary>Docmost refused the API key.</summary>
    Unauthorized = 1,

    /// <summary>Docmost did not answer: down, wrong address, or blocked.</summary>
    Unavailable = 2,
}

/// <summary>How a page came to be linked to a ticket.</summary>
public enum DocLinkSource
{
    /// <summary>The page mentions the ticket; Docmost reports it on every save.</summary>
    Mention = 0,

    /// <summary>Someone linked the page from the ticket, or created it from there.</summary>
    Manual = 1,
}

public class DocsConnectionDto
{
    public required bool Configured { get; set; }

    public required string? BaseUrl { get; set; }

    public required DocsConnectionStatus Status { get; set; }

    public required DocsProblem Problem { get; set; }

    public required DateTime? LastCheckedAt { get; set; }

    /// <summary>Who the API key acts as in Docmost, from the last successful check.</summary>
    public required string? DocmostUser { get; set; }

    /// <summary>
    /// Sprint Boot's own address: it goes into the Sprint Boot integration in Docmost, which calls the bridge under it.
    /// </summary>
    public required string SprintBootUrl { get; set; }

    /// <summary>What Docmost answered on the last failed check, to tell a wrong key from a restriction.</summary>
    public required string? ProblemDetail { get; set; }

    public required bool HasBridgeSecret { get; set; }
}

public class SaveDocsConnectionInput
{
    [Required]
    [Url]
    [StringLength(512)]
    public required string BaseUrl { get; set; }

    /// <summary>Blank keeps the stored key.</summary>
    [StringLength(4096)]
    public string? ApiKey { get; set; }
}

/// <summary>Shown once, when it is made: only its encrypted form is kept.</summary>
public class DocsBridgeSecretDto
{
    public required string Secret { get; set; }

    public required string SprintBootUrl { get; set; }
}

public class DocmostSpaceDto
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }
}

public class ProjectDocsDto
{
    /// <summary>False until an administrator connects Docmost.</summary>
    public required bool Configured { get; set; }

    public required DocmostSpaceDto? Space { get; set; }

    /// <summary>Opens the linked Docmost space.</summary>
    public required string? SpaceUrl { get; set; }

    public required bool CanManage { get; set; }
}

public class LinkDocsSpaceInput
{
    [Required]
    public required string DocmostSpaceId { get; set; }
}

public class DocsPageNodeDto
{
    public required string Id { get; set; }

    public required string SlugId { get; set; }

    public required string Title { get; set; }

    public required string? Icon { get; set; }

    public required bool HasChildren { get; set; }

    public required string Url { get; set; }
}

public class TicketDocPageDto
{
    public required Guid Id { get; set; }

    public required string PageId { get; set; }

    public required string Title { get; set; }

    public required string Url { get; set; }

    public required string? SpaceName { get; set; }

    public required DocLinkSource Source { get; set; }

    public required DateTime UpdatedAt { get; set; }
}

public class TicketDocsDto
{
    public required bool Configured { get; set; }

    /// <summary>The ticket's space is linked to a Docmost space and the caller can edit tickets.</summary>
    public required bool CanCreatePage { get; set; }

    public required bool CanEdit { get; set; }

    /// <summary>One row per page: a page both mentioned and linked shows as linked.</summary>
    public required List<TicketDocPageDto> Pages { get; set; }
}

public class LinkDocPageInput
{
    [Required]
    [StringLength(2048)]
    public required string Url { get; set; }
}

public interface IDocsAdminAppService : IApplicationService
{
    Task<DocsConnectionDto> GetAsync();

    /// <summary>Saves and checks the connection; a failed check is saved too, with its problem.</summary>
    Task<DocsConnectionDto> SaveAsync(SaveDocsConnectionInput input);

    Task<DocsConnectionDto> TestAsync();

    /// <summary>Makes a new secret for Docmost's calls; the old one stops working at once.</summary>
    Task<DocsBridgeSecretDto> RotateSecretAsync();
}

public interface IDocsProjectAppService : IApplicationService
{
    Task<ProjectDocsDto> GetAsync(Guid projectId);

    /// <summary>The Docmost spaces the connection can see, to pick one for the space.</summary>
    Task<List<DocmostSpaceDto>> GetDocmostSpacesAsync(Guid projectId);

    Task<ProjectDocsDto> LinkAsync(Guid projectId, LinkDocsSpaceInput input);

    Task UnlinkAsync(Guid projectId);

    /// <summary>One level of the linked space's page tree; no parent means its top pages.</summary>
    Task<List<DocsPageNodeDto>> GetPagesAsync(Guid projectId, string? parentPageId);
}

public interface IDocsTicketAppService : IApplicationService
{
    Task<TicketDocsDto> GetListAsync(Guid ticketId);

    /// <summary>Links a page by its Docmost address.</summary>
    Task<TicketDocPageDto> LinkAsync(Guid ticketId, LinkDocPageInput input);

    /// <summary>Removes a link made by hand; links from mentions follow the page.</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Creates a page for the ticket in the linked Docmost space, with a card that links back.</summary>
    Task<TicketDocPageDto> CreatePageAsync(Guid ticketId);
}

public static class DocsErrors
{
    public const string NotConfigured = "SprintBoot:DocsNotConfigured";
    public const string Unavailable = "SprintBoot:DocsUnavailable";
    public const string Unauthorized = "SprintBoot:DocsUnauthorized";
    public const string SpaceNotLinked = "SprintBoot:DocsSpaceNotLinked";
    public const string InvalidPageUrl = "SprintBoot:DocsInvalidPageUrl";
    public const string PageNotFound = "SprintBoot:DocsPageNotFound";
    public const string AlreadyLinked = "SprintBoot:DocsPageAlreadyLinked";
    public const string MentionLink = "SprintBoot:DocsMentionLink";
    public const string SpaceNotFound = "SprintBoot:DocsSpaceNotFound";
    public const string ApiKeyRequired = "SprintBoot:DocsApiKeyRequired";

    /// <summary>Docmost acted for someone with no active Sprint Boot account under that email.</summary>
    public const string UnknownUser = "SprintBoot:DocsUnknownUser";
}
