using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Tickets;
using SprintBoot.Workflows;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Security.Claims;
using Volo.Abp.Timing;

namespace SprintBoot.DocsIntegration;

public class BridgeUnfurlInput
{
    public string[]? Keys { get; set; }

    /// <summary>Sprint Boot ticket addresses, /browse/KEY-12; read for their keys.</summary>
    public string[]? Urls { get; set; }
}

public class BridgeTicketView
{
    public required string Key { get; set; }

    public required string Title { get; set; }

    public required string Url { get; set; }

    public required string Status { get; set; }

    /// <summary>todo, inProgress or done, for the chip's colour.</summary>
    public required string StatusCategory { get; set; }

    public required string Type { get; set; }

    public required string TypeIcon { get; set; }

    public required string Priority { get; set; }

    public required string? Assignee { get; set; }

    public required string? AssigneeAvatarUrl { get; set; }

    public required string SpaceKey { get; set; }

    public required bool Archived { get; set; }
}

public class BridgePageLinksInput
{
    public string PageId { get; set; } = null!;

    public string? Title { get; set; }

    public string? Url { get; set; }

    public string? SpaceName { get; set; }

    public string[]? Keys { get; set; }

    public string[]? Urls { get; set; }

    /// <summary>The page went to Docmost's trash or was deleted: it no longer belongs with any ticket.</summary>
    public bool Deleted { get; set; }
}

public class BridgeCreateTicketInput
{
    public string ActorEmail { get; set; } = null!;

    public string SpaceKey { get; set; } = null!;

    /// <summary>A ticket type name of the space; blank picks its first standard type.</summary>
    public string? Type { get; set; }

    public string Title { get; set; } = null!;

    /// <summary>Plain text; each line becomes a paragraph.</summary>
    public string? Description { get; set; }
}

public class BridgeSearchInput
{
    public string ActorEmail { get; set; } = null!;

    public string? Text { get; set; }

    public string? SpaceKey { get; set; }

    public int Limit { get; set; } = 10;
}

public class BridgeSpacesInput
{
    public string ActorEmail { get; set; } = null!;
}

/// <summary>What Docmost's Sprint Boot integration asks of Sprint Boot.</summary>
public partial class DocsBridge(
    DocsGateway gateway,
    IRepository<TicketDocLink, Guid> links,
    IRepository<ProjectDocsSpace, Guid> spaces,
    ITicketKeyResolver resolver,
    ITicketPlanningGateway tickets,
    ITicketAppService ticketService,
    IProjectAccess projectAccess,
    IUserImpersonation impersonation,
    ICurrentPrincipalAccessor principal,
    IGuidGenerator guids,
    IClock clock) : ITransientDependency
{
    private const int MaxKeys = 200;

    /// <summary>True when the bearer secret is the one Sprint Boot gave out last.</summary>
    public async Task<bool> AcceptsAsync(string? authorization)
    {
        const string prefix = "Bearer ";
        if (authorization?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        var stored = (await gateway.FindAsync())?.BridgeSecretHash;
        if (stored is null)
        {
            return false;
        }

        var presented = DocsGateway.HashSecret(authorization[prefix.Length..].Trim());
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(presented), Encoding.ASCII.GetBytes(stored));
    }

    /// <summary>Ticket facts for smart links, on the shared token: whoever can read the page sees them.</summary>
    public async Task<List<BridgeTicketView>> UnfurlAsync(BridgeUnfurlInput input)
    {
        var refs = await resolver.ResolveAsync(Keys(input.Keys, input.Urls));
        var spaceRefs = await projectAccess.GetRefsAsync(refs.Values.Select(item => item.ProjectId));
        var views = new List<BridgeTicketView>();
        foreach (var group in refs.Values.GroupBy(item => item.ProjectId))
        {
            if (!spaceRefs.TryGetValue(group.Key, out var space))
            {
                continue;
            }

            foreach (var card in await tickets.GetCardsAsync(group.Key, [.. group.Select(item => item.TicketId)]))
            {
                views.Add(new BridgeTicketView
                {
                    Key = card.Key,
                    Title = card.Title,
                    Url = gateway.TicketUrl(card.Key),
                    Status = card.Status.Name,
                    StatusCategory = card.Status.Category switch
                    {
                        StatusCategory.Done => "done",
                        StatusCategory.InProgress => "inProgress",
                        _ => "todo",
                    },
                    Type = card.Type.Name,
                    TypeIcon = card.Type.Icon,
                    Priority = card.Priority.Name,
                    Assignee = card.Assignee?.Name,
                    AssigneeAvatarUrl = card.Assignee?.AvatarUrl,
                    SpaceKey = space.Key,
                    Archived = space.IsArchived,
                });
            }
        }

        return views;
    }

    /// <summary>
    /// Docmost sends every ticket a page mentions on each save; the page's mention links become exactly those. Links
    /// of archived spaces stay as they were, since nothing about an archived space changes.
    /// </summary>
    public async Task<int> SetPageLinksAsync(BridgePageLinksInput input)
    {
        if (string.IsNullOrWhiteSpace(input.PageId))
        {
            throw new BusinessException(DocsErrors.PageNotFound);
        }

        var existing = await links.GetListAsync(item => item.PageId == input.PageId);
        var active = new Dictionary<Guid, bool>();
        async Task<bool> ActiveAsync(Guid projectId) =>
            active.TryGetValue(projectId, out var known) ? known : active[projectId] = await projectAccess.IsActiveAsync(projectId);

        if (input.Deleted)
        {
            foreach (var link in existing)
            {
                if (await ActiveAsync(link.ProjectId))
                {
                    await links.DeleteAsync(link);
                }
            }

            return 0;
        }

        var refs = await resolver.ResolveAsync(Keys(input.Keys, input.Urls));

        var wanted = new List<TicketRef>();
        foreach (var item in refs.Values)
        {
            if (await ActiveAsync(item.ProjectId))
            {
                wanted.Add(item);
            }
        }

        var now = clock.Now;
        var title = string.IsNullOrWhiteSpace(input.Title) ? "Untitled" : input.Title.Trim();
        title = title.Length > 512 ? title[..512] : title;
        var url = input.Url?.Trim() ?? string.Empty;

        var kept = new List<TicketDocLink>();
        foreach (var link in existing)
        {
            if (link.Source == DocLinkSource.Mention && wanted.All(item => item.TicketId != link.TicketId) && await ActiveAsync(link.ProjectId))
            {
                await links.DeleteAsync(link);
            }
            else
            {
                kept.Add(link);
            }
        }

        // Updating a deleted row would bring it back, so only the kept ones follow the page's title.
        foreach (var link in kept)
        {
            link.Title = title;
            link.Url = url.Length > 0 ? url : link.Url;
            link.SpaceName = input.SpaceName ?? link.SpaceName;
            link.UpdatedAt = now;
            await links.UpdateAsync(link);
        }

        foreach (var item in wanted.Where(item => !existing.Any(link => link.Source == DocLinkSource.Mention && link.TicketId == item.TicketId)))
        {
            await links.InsertAsync(new TicketDocLink(guids.Create(), item.TicketId, item.ProjectId, input.PageId, DocLinkSource.Mention, title, url, input.SpaceName, now));
        }

        return wanted.Count;
    }

    /// <summary>Creates a ticket as the Docmost user, so their permissions decide.</summary>
    public async Task<BridgeTicketView> CreateTicketAsync(BridgeCreateTicketInput input)
    {
        using var actor = principal.Change(await ActorAsync(input.ActorEmail));
        var space = await projectAccess.FindRefByKeyAsync(input.SpaceKey?.Trim().ToUpperInvariant() ?? string.Empty)
            ?? throw new BusinessException(DocsErrors.SpaceNotFound);
        var types = await ticketService.GetTypesAsync(space.Id);
        var type = (string.IsNullOrWhiteSpace(input.Type)
                ? types.FirstOrDefault(item => item.Hierarchy == TicketHierarchy.Standard)
                : types.FirstOrDefault(item => string.Equals(item.Name, input.Type.Trim(), StringComparison.OrdinalIgnoreCase)))
            ?? types.First(item => item.Hierarchy == TicketHierarchy.Standard);

        var title = input.Title?.Trim() ?? string.Empty;
        var ticket = await ticketService.CreateAsync(new CreateTicketInput
        {
            ProjectId = space.Id,
            TypeId = type.Id,
            Title = title.Length > 255 ? title[..255] : title,
            Description = Paragraphs(input.Description),
        });
        return (await UnfurlAsync(new BridgeUnfurlInput { Keys = [ticket.Key] })).Single();
    }

    /// <summary>Tickets the Docmost user can see, to insert as smart links.</summary>
    public async Task<List<BridgeTicketView>> SearchAsync(BridgeSearchInput input)
    {
        using var actor = principal.Change(await ActorAsync(input.ActorEmail));
        Guid? projectId = string.IsNullOrWhiteSpace(input.SpaceKey)
            ? null
            : (await projectAccess.FindRefByKeyAsync(input.SpaceKey.Trim().ToUpperInvariant()))?.Id;
        var found = await ticketService.GetSearchAsync(new TicketSearchInput
        {
            Text = input.Text,
            ProjectId = projectId,
            ExcludeArchived = true,
            MaxResultCount = Math.Clamp(input.Limit, 1, 50),
        });
        if (found.Items.Count == 0)
        {
            return [];
        }

        // Unfurling groups by space; the search's own order is the one to keep.
        var views = (await UnfurlAsync(new BridgeUnfurlInput { Keys = [.. found.Items.Select(item => item.Key)] })).ToDictionary(view => view.Key);
        return [.. found.Items.Where(item => views.ContainsKey(item.Key)).Select(item => views[item.Key])];
    }

    /// <summary>The spaces the Docmost user can create tickets in, with the Docmost space each keeps its documents in.</summary>
    public async Task<List<object>> GetSpacesAsync(BridgeSpacesInput input)
    {
        using var actor = principal.Change(await ActorAsync(input.ActorEmail));
        var visible = await projectAccess.GetVisibleProjectIdsAsync(includeArchived: false);
        var refs = await projectAccess.GetRefsAsync(visible);
        var linked = (await spaces.GetListAsync(item => visible.Contains(item.ProjectId))).ToDictionary(item => item.ProjectId);
        var result = new List<object>();
        foreach (var space in refs.Values.OrderBy(item => item.Name))
        {
            if (await projectAccess.IsGrantedAsync(space.Id, ProjectPermissions.EditTickets))
            {
                result.Add(new { space.Key, space.Name, DocmostSpaceId = linked.GetValueOrDefault(space.Id)?.DocmostSpaceId });
            }
        }

        return result;
    }

    private async Task<System.Security.Claims.ClaimsPrincipal> ActorAsync(string? email) =>
        await impersonation.FindPrincipalAsync(email ?? string.Empty) ?? throw new BusinessException(DocsErrors.UnknownUser);

    private static List<string> Keys(string[]? keys, string[]? urls) =>
        [.. (keys ?? []).Select(key => key.Trim().ToUpperInvariant())
            .Concat((urls ?? []).Select(url => BrowsePath().Match(url)).Where(match => match.Success).Select(match => match.Groups["key"].Value.ToUpperInvariant()))
            .Where(key => TicketKey().IsMatch(key))
            .Distinct()
            .Take(MaxKeys)];

    private static string? Paragraphs(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : string.Concat(text.Replace("\r\n", "\n").Split('\n').Where(line => line.Trim().Length > 0).Select(line => $"<p>{WebUtility.HtmlEncode(line.Trim())}</p>"));

    [GeneratedRegex(@"/browse/(?<key>[A-Za-z][A-Za-z0-9]{1,9}-[1-9][0-9]*)(?![A-Za-z0-9])")]
    private static partial Regex BrowsePath();

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,9}-[1-9][0-9]*$")]
    private static partial Regex TicketKey();
}

/// <summary>
/// Docmost's way in. Anonymous to MVC: each call proves itself with the bridge secret. Actions return objects, not
/// IActionResult, so ABP turns refusals into the usual JSON errors.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/docs-bridge")]
[AllowAnonymous]
public class DocsBridgeController(DocsBridge bridge) : ControllerBase
{
    private Task<bool> AcceptedAsync() => bridge.AcceptsAsync(Request.Headers.Authorization.ToString());

    [HttpPost("unfurl")]
    public async Task<ActionResult<object>> UnfurlAsync([FromBody] BridgeUnfurlInput input) =>
        await AcceptedAsync() ? (ActionResult<object>)new { tickets = await bridge.UnfurlAsync(input) } : Unauthorized();

    [HttpPost("page-links")]
    public async Task<ActionResult<object>> PageLinksAsync([FromBody] BridgePageLinksInput input) =>
        await AcceptedAsync() ? (ActionResult<object>)new { linked = await bridge.SetPageLinksAsync(input) } : Unauthorized();

    [HttpPost("tickets")]
    public async Task<ActionResult<object>> CreateTicketAsync([FromBody] BridgeCreateTicketInput input) =>
        await AcceptedAsync() ? (ActionResult<object>)await bridge.CreateTicketAsync(input) : Unauthorized();

    [HttpPost("search")]
    public async Task<ActionResult<object>> SearchAsync([FromBody] BridgeSearchInput input) =>
        await AcceptedAsync() ? (ActionResult<object>)new { tickets = await bridge.SearchAsync(input) } : Unauthorized();

    [HttpPost("spaces")]
    public async Task<ActionResult<object>> SpacesAsync([FromBody] BridgeSpacesInput input) =>
        await AcceptedAsync() ? (ActionResult<object>)new { spaces = await bridge.GetSpacesAsync(input) } : Unauthorized();
}
