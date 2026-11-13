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

    /// <summary>The ticket's description or a comment holds a chip for the page; saving the text keeps it current.</summary>
    Text = 2,
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

    /// <summary>Pages linked by hand or by a chip in the ticket's text; one row per page, a hand link wins.</summary>
    public required List<TicketDocPageDto> Linked { get; set; }

    /// <summary>Docmost pages that link to the ticket.</summary>
    public required List<TicketDocPageDto> Mentions { get; set; }
}

/// <summary>A Docmost page found by title, to link or to create under.</summary>
public class DocsPageHitDto
{
    public required string Id { get; set; }

    public required string Title { get; set; }

    public required string? SpaceName { get; set; }

    public required string Url { get; set; }
}

public class CreateDocPageInput
{
    /// <summary>Blank uses the ticket's key and title.</summary>
    [StringLength(255)]
    public string? Title { get; set; }

    /// <summary>A page of the space's Docmost space; none puts the page at its top.</summary>
    [StringLength(64)]
    public string? ParentPageId { get; set; }
}

/// <summary>What a chip for a Docmost page shows.</summary>
public class DocsPageRefDto
{
    public required string PageId { get; set; }

    public required string Title { get; set; }

    public required string? SpaceName { get; set; }

    public required string Url { get; set; }
}

/// <summary>For every signed-in person: whether Docmost is connected and where, so editors can recognize its links.</summary>
public class DocsInfoDto
{
    public required bool Configured { get; set; }

    public required string? DocmostUrl { get; set; }
}

/// <summary>A page picked from search, by id, or pasted, by address.</summary>
public class LinkDocPageInput
{
    [StringLength(2048)]
    public string? Url { get; set; }

    [StringLength(64)]
    public string? PageId { get; set; }
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

    /// <summary>Pages of the linked Docmost space whose title matches, to pick a parent.</summary>
    Task<List<DocsPageHitDto>> GetSearchPagesAsync(Guid projectId, string? query);
}

public interface IDocsTicketAppService : IApplicationService
{
    Task<TicketDocsDto> GetListAsync(Guid ticketId);

    /// <summary>Links a page picked from search or pasted by its Docmost address.</summary>
    Task<TicketDocPageDto> LinkAsync(Guid ticketId, LinkDocPageInput input);

    /// <summary>Pages of every Docmost space the connection can read whose title matches.</summary>
    Task<List<DocsPageHitDto>> GetSearchPagesAsync(Guid ticketId, string? query);

    /// <summary>Removes a link made by hand; links from mentions follow the page.</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Creates a page for the ticket in the linked Docmost space, with a card that links back.</summary>
    Task<TicketDocPageDto> CreatePageAsync(Guid ticketId, CreateDocPageInput input);
}

public interface IDocsPageAppService : IApplicationService
{
    Task<DocsInfoDto> GetInfoAsync();

    /// <summary>The page a Docmost address opens, for a chip; 404 when it is not one.</summary>
    Task<DocsPageRefDto> GetResolveAsync(string url);
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
