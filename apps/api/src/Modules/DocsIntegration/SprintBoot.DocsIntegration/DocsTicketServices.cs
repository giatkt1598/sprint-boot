using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using SprintBoot.Projects;
using SprintBoot.Tickets;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace SprintBoot.DocsIntegration;

/// <summary>Search hits as the UI lists them.</summary>
public static class DocsHits
{
    public static List<DocsPageHitDto> From(
        IEnumerable<(DocmostPage Page, DocmostSpaceDto? Space)> hits, DocsConnection connection, string? fallbackSlug = null, string? fallbackName = null) =>
        [.. hits.Select(hit =>
        {
            var slug = hit.Space?.Slug is { Length: > 0 } found ? found : fallbackSlug;
            return new DocsPageHitDto
            {
                Id = hit.Page.Id,
                Title = hit.Page.Title,
                SpaceName = hit.Space?.Name is { Length: > 0 } name ? name : fallbackName,
                Url = slug is null
                    ? $"{connection.BaseUrl.TrimEnd('/')}/p/{hit.Page.SlugId}"
                    : DocmostClient.PageUrl(connection.BaseUrl, slug, hit.Page.SlugId, hit.Page.Title),
            };
        })];
}

[Authorize]
public class DocsTicketAppService(
    IRepository<TicketDocLink, Guid> links,
    IRepository<ProjectDocsSpace, Guid> spaces,
    DocsGateway gateway,
    DocmostClient client,
    IProjectAccess projectAccess,
    ITicketPlanningGateway tickets,
    IClock clock)
    : ApplicationService, IDocsTicketAppService
{
    /// <summary>The provider name Docmost's Sprint Boot integration renders cards with.</summary>
    public const string DocmostProvider = "sprint-boot";

    public async Task<TicketDocsDto> GetListAsync(Guid ticketId)
    {
        var projectId = await tickets.GetProjectIdAsync(ticketId);
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);
        var canEdit = await projectAccess.IsGrantedAsync(projectId, ProjectPermissions.EditTickets);
        var configured = await gateway.IsConfiguredAsync();
        var rows = await links.GetListAsync(item => item.TicketId == ticketId);

        return new TicketDocsDto
        {
            Configured = configured,
            CanEdit = canEdit,
            CanCreatePage = configured && canEdit && await spaces.AnyAsync(item => item.ProjectId == projectId),
            Linked = [.. rows
                .Where(item => item.Source != DocLinkSource.Mention)
                .GroupBy(item => item.PageId)
                // A hand link can be removed; a chip only goes with the text it is in.
                .Select(group => group.OrderBy(item => item.Source == DocLinkSource.Manual ? 0 : 1).First())
                .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(ToDto)],
            Mentions = [.. rows
                .Where(item => item.Source == DocLinkSource.Mention)
                .OrderByDescending(item => item.UpdatedAt)
                .Select(ToDto)],
        };
    }

    public async Task<List<DocsPageHitDto>> GetSearchPagesAsync(Guid ticketId, string? query)
    {
        var projectId = await tickets.GetProjectIdAsync(ticketId);
        await projectAccess.CheckAsync(projectId, ProjectPermissions.EditTickets);
        return string.IsNullOrWhiteSpace(query)
            ? []
            : await gateway.CallAsync(async (access, connection) =>
                DocsHits.From(await client.SearchPagesAsync(access, query.Trim(), null), connection));
    }

    public async Task<TicketDocPageDto> LinkAsync(Guid ticketId, LinkDocPageInput input)
    {
        var projectId = await tickets.GetProjectIdAsync(ticketId);
        await projectAccess.CheckAsync(projectId, ProjectPermissions.EditTickets);
        var connection = await gateway.FindAsync() ?? throw new BusinessException(DocsErrors.NotConfigured);
        var target = !string.IsNullOrWhiteSpace(input.PageId)
            ? input.PageId.Trim()
            : DocmostClient.SlugIdFromUrl(input.Url ?? string.Empty, connection.BaseUrl)
                ?? throw new BusinessException(DocsErrors.InvalidPageUrl);

        var found = await gateway.CallAsync((access, _) => client.FindPageAsync(access, target))
            ?? throw new BusinessException(DocsErrors.PageNotFound);
        var (page, space) = found;
        if (await links.AnyAsync(item => item.TicketId == ticketId && item.PageId == page.Id && item.Source == DocLinkSource.Manual))
        {
            throw new BusinessException(DocsErrors.AlreadyLinked);
        }

        var link = await links.InsertAsync(new TicketDocLink(
            GuidGenerator.Create(), ticketId, projectId, page.Id, DocLinkSource.Manual, page.Title,
            DocmostClient.PageUrl(connection.BaseUrl, space.Slug, page.SlugId, page.Title), space.Name, clock.Now), autoSave: true);
        return ToDto(link);
    }

    public async Task DeleteAsync(Guid id)
    {
        var link = await links.GetAsync(id);
        await projectAccess.CheckAsync(link.ProjectId, ProjectPermissions.EditTickets);
        if (link.Source != DocLinkSource.Manual)
        {
            throw new BusinessException(DocsErrors.MentionLink);
        }

        await links.DeleteAsync(link);
    }

    public async Task<TicketDocPageDto> CreatePageAsync(Guid ticketId, CreateDocPageInput input)
    {
        var projectId = await tickets.GetProjectIdAsync(ticketId);
        await projectAccess.CheckAsync(projectId, ProjectPermissions.EditTickets);
        var space = await spaces.FirstOrDefaultAsync(item => item.ProjectId == projectId)
            ?? throw new BusinessException(DocsErrors.SpaceNotLinked);
        var card = await tickets.FindCardAsync(ticketId) ?? throw new EntityNotFoundException(typeof(TicketDocLink), ticketId);

        var title = string.IsNullOrWhiteSpace(input.Title) ? $"{card.Key} {card.Title}" : input.Title.Trim();
        var parentPageId = string.IsNullOrWhiteSpace(input.ParentPageId) ? null : input.ParentPageId.Trim();
        var ticketUrl = gateway.TicketUrl(card.Key);
        // Docmost renders the card through its Sprint Boot integration; without it, the card is still a plain link.
        var content = new
        {
            type = "doc",
            content = new object[]
            {
                new { type = "integrationCard", attrs = new { url = ticketUrl, provider = DocmostProvider } },
                new { type = "paragraph" },
            },
        };

        var (link, created) = await gateway.CallAsync(async (access, connection) =>
        {
            var page = await client.CreatePageAsync(access, space.DocmostSpaceId, parentPageId, title, content);
            return (DocmostClient.PageUrl(connection.BaseUrl, space.Slug, page.SlugId, page.Title), page);
        });

        var row = await links.InsertAsync(new TicketDocLink(
            GuidGenerator.Create(), ticketId, projectId, created.Id, DocLinkSource.Manual, created.Title, link, space.Name, clock.Now), autoSave: true);
        return ToDto(row);
    }

    public static TicketDocPageDto ToDto(TicketDocLink link) => new()
    {
        Id = link.Id,
        PageId = link.PageId,
        Title = link.Title,
        Url = link.Url,
        SpaceName = link.SpaceName,
        Source = link.Source,
        UpdatedAt = link.UpdatedAt,
    };
}

[Authorize]
public class DocsPageAppService(DocsGateway gateway, DocmostClient client) : ApplicationService, IDocsPageAppService
{
    public async Task<DocsInfoDto> GetInfoAsync()
    {
        var connection = await gateway.FindAsync();
        return new DocsInfoDto
        {
            Configured = connection?.EncryptedApiKey is not null,
            DocmostUrl = connection?.EncryptedApiKey is null ? null : connection.BaseUrl,
        };
    }

    public async Task<DocsPageRefDto> GetResolveAsync(string url)
    {
        var connection = await gateway.FindAsync() ?? throw new BusinessException(DocsErrors.NotConfigured);
        var slugId = DocmostClient.SlugIdFromUrl(url ?? string.Empty, connection.BaseUrl)
            ?? throw new EntityNotFoundException(typeof(TicketDocLink), url);
        var found = await gateway.CallAsync((access, _) => client.FindPageAsync(access, slugId))
            ?? throw new EntityNotFoundException(typeof(TicketDocLink), url);
        var (page, space) = found;
        return new DocsPageRefDto
        {
            PageId = page.Id,
            Title = page.Title,
            SpaceName = space.Name,
            Url = DocmostClient.PageUrl(connection.BaseUrl, space.Slug, page.SlugId, page.Title),
        };
    }
}

/// <summary>
/// Chips for Docmost pages in a ticket's description and comments become the ticket's Text links: saving the text
/// adds the new ones and drops the ones taken out. Hand links are left alone.
/// </summary>
public partial class TicketTextChangedHandler(
    IRepository<TicketDocLink, Guid> links,
    ITicketRichTextReader texts,
    IProjectAccess projectAccess,
    IGuidGenerator guids,
    IClock clock)
    : IDistributedEventHandler<TicketTextChangedEto>, ITransientDependency
{
    [UnitOfWork(isTransactional: true)]
    public virtual async Task HandleEventAsync(TicketTextChangedEto eventData)
    {
        // An archived space is read-only, and its links stay as they were.
        if (!await projectAccess.IsActiveAsync(eventData.ProjectId))
        {
            return;
        }

        var chips = Chips(await texts.GetAsync(eventData.TicketId));
        var existing = await links.GetListAsync(item => item.TicketId == eventData.TicketId && item.Source == DocLinkSource.Text);
        var now = clock.Now;

        foreach (var link in existing.Where(link => !chips.ContainsKey(link.PageId)))
        {
            await links.DeleteAsync(link);
        }

        foreach (var (pageId, chip) in chips)
        {
            var link = existing.FirstOrDefault(item => item.PageId == pageId);
            if (link is null)
            {
                await links.InsertAsync(new TicketDocLink(
                    guids.Create(), eventData.TicketId, eventData.ProjectId, pageId, DocLinkSource.Text, chip.Title, chip.Url, null, now));
            }
            else if (link.Title != chip.Title || link.Url != chip.Url)
            {
                link.Title = chip.Title;
                link.Url = chip.Url;
                link.UpdatedAt = now;
                await links.UpdateAsync(link);
            }
        }
    }

    /// <summary>Page id to what the chip shows, from &lt;a data-type="doc-mention" data-page-id href&gt;Title&lt;/a&gt;.</summary>
    public static Dictionary<string, (string Title, string Url)> Chips(IEnumerable<string> htmls)
    {
        var chips = new Dictionary<string, (string Title, string Url)>();
        foreach (var html in htmls.Where(text => text.Contains("doc-mention", StringComparison.Ordinal)))
        {
            foreach (Match chip in ChipPattern().Matches(html))
            {
                var tag = chip.Groups["tag"].Value;
                if (Attribute(tag, "data-type") != "doc-mention")
                {
                    continue;
                }

                var pageId = Attribute(tag, "data-page-id")?.Trim() ?? string.Empty;
                var url = Attribute(tag, "href") ?? string.Empty;
                var title = WebUtility.HtmlDecode(Markup().Replace(chip.Groups["text"].Value, string.Empty)).Trim();
                if (pageId.Length is > 0 and <= 64 && url.Length is > 0 and <= 2048)
                {
                    chips.TryAdd(pageId, (title.Length == 0 ? "Untitled" : title.Length > 512 ? title[..512] : title, url));
                }
            }
        }

        return chips;
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $@"\s{Regex.Escape(name)}=""(?<value>[^""]*)""");
        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
    }

    // The sanitizer writes every attribute in double quotes, in any order.
    [GeneratedRegex(@"<a(?<tag>\s[^>]*)>(?<text>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ChipPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Markup();
}
