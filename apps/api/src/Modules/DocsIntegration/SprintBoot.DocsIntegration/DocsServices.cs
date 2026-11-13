using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using SprintBoot.Identity;
using SprintBoot.Projects;
using SprintBoot.Tickets;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace SprintBoot.DocsIntegration;

/// <summary>The stored connection, and every call to Docmost made through it.</summary>
public class DocsGateway(
    IRepository<DocsConnection, Guid> connections,
    DocmostClient client,
    IStringEncryptionService encryption,
    IUnitOfWorkManager unitOfWork,
    IConfiguration configuration,
    IClock clock) : ITransientDependency
{
    public Task<DocsConnection?> FindAsync() => connections.FirstOrDefaultAsync(_ => true);

    public async Task<bool> IsConfiguredAsync() => await FindAsync() is { EncryptedApiKey: not null };

    /// <summary>Where Sprint Boot is opened from; ticket links and the bridge address hang off it.</summary>
    public string PublicUrl =>
        new[] { configuration["Docs:PublicUrl"], configuration["Auth:SpaBaseUrl"], "http://localhost:5173" }
            .First(value => !string.IsNullOrWhiteSpace(value))!.TrimEnd('/');

    public string TicketUrl(string key) => $"{PublicUrl}/browse/{key}";

    public DocmostAccess Access(DocsConnection connection) =>
        new(connection.BaseUrl, encryption.Decrypt(connection.EncryptedApiKey!)!);

    /// <summary>
    /// Runs one Docmost call. A refused key marks the connection, in its own transaction so the request's failure
    /// does not roll the mark back; every failure becomes a business error the UI explains.
    /// </summary>
    public async Task<T> CallAsync<T>(Func<DocmostAccess, DocsConnection, Task<T>> call)
    {
        var connection = await FindAsync();
        if (connection?.EncryptedApiKey is null)
        {
            throw new BusinessException(DocsErrors.NotConfigured);
        }

        try
        {
            return await call(Access(connection), connection);
        }
        catch (DocmostAuthException failure)
        {
            await MarkAsync(connection.Id, DocsProblem.Unauthorized, failure.Message);
            throw DocmostFailures.ToBusiness(failure);
        }
        catch (DocmostUnavailableException failure)
        {
            throw DocmostFailures.ToBusiness(failure);
        }
    }

    /// <summary>Checks the connection and records the outcome on it.</summary>
    public async Task CheckAsync(DocsConnection connection)
    {
        connection.LastCheckedAt = clock.Now;
        try
        {
            connection.DocmostUser = await client.VerifyAsync(Access(connection));
            connection.Status = DocsConnectionStatus.Connected;
            connection.Problem = DocsProblem.None;
            connection.ProblemDetail = null;
        }
        catch (Exception failure) when (failure is DocmostAuthException or DocmostUnavailableException or DocmostNotFoundException)
        {
            connection.Status = DocsConnectionStatus.NeedsAttention;
            connection.Problem = failure is DocmostAuthException ? DocsProblem.Unauthorized : DocsProblem.Unavailable;
            connection.ProblemDetail = failure.Message.Length > 512 ? failure.Message[..512] : failure.Message;
        }

        await connections.UpdateAsync(connection, autoSave: true);
    }

    public static string HashSecret(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private async Task MarkAsync(Guid id, DocsProblem problem, string detail)
    {
        using var uow = unitOfWork.Begin(requiresNew: true);
        var connection = await connections.GetAsync(id);
        connection.Status = DocsConnectionStatus.NeedsAttention;
        connection.Problem = problem;
        connection.ProblemDetail = detail.Length > 512 ? detail[..512] : detail;
        connection.LastCheckedAt = clock.Now;
        await connections.UpdateAsync(connection);
        await uow.CompleteAsync();
    }
}

[Authorize(SprintBootPermissions.ManageDocs)]
public class DocsAdminAppService(
    IRepository<DocsConnection, Guid> connections,
    DocsGateway gateway,
    IStringEncryptionService encryption)
    : ApplicationService, IDocsAdminAppService
{
    public async Task<DocsConnectionDto> GetAsync() => ToDto(await gateway.FindAsync());

    public async Task<DocsConnectionDto> SaveAsync(SaveDocsConnectionInput input)
    {
        var baseUrl = input.BaseUrl.Trim().TrimEnd('/');
        var connection = await gateway.FindAsync();
        var key = string.IsNullOrWhiteSpace(input.ApiKey) ? null : input.ApiKey.Trim();
        if (key is null && connection?.EncryptedApiKey is null)
        {
            throw new BusinessException(DocsErrors.ApiKeyRequired);
        }

        if (connection is null)
        {
            connection = await connections.InsertAsync(new DocsConnection(GuidGenerator.Create(), baseUrl), autoSave: true);
        }

        connection.BaseUrl = baseUrl;
        if (key is not null)
        {
            connection.EncryptedApiKey = encryption.Encrypt(key);
        }

        await gateway.CheckAsync(connection);
        return ToDto(connection);
    }

    public async Task<DocsConnectionDto> TestAsync()
    {
        var connection = await gateway.FindAsync();
        if (connection?.EncryptedApiKey is null)
        {
            throw new BusinessException(DocsErrors.NotConfigured);
        }

        await gateway.CheckAsync(connection);
        return ToDto(connection);
    }

    public async Task<DocsBridgeSecretDto> RotateSecretAsync()
    {
        var connection = await gateway.FindAsync() ?? throw new BusinessException(DocsErrors.NotConfigured);
        var secret = $"sbd_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";
        connection.BridgeSecretHash = DocsGateway.HashSecret(secret);
        await connections.UpdateAsync(connection, autoSave: true);
        return new DocsBridgeSecretDto { Secret = secret, SprintBootUrl = gateway.PublicUrl };
    }

    private DocsConnectionDto ToDto(DocsConnection? connection) => new()
    {
        Configured = connection?.EncryptedApiKey is not null,
        BaseUrl = connection?.BaseUrl,
        Status = connection?.Status ?? DocsConnectionStatus.NeedsAttention,
        Problem = connection?.Problem ?? DocsProblem.None,
        LastCheckedAt = connection?.LastCheckedAt,
        DocmostUser = connection?.DocmostUser,
        SprintBootUrl = gateway.PublicUrl,
        ProblemDetail = connection?.ProblemDetail,
        HasBridgeSecret = connection?.BridgeSecretHash is not null,
    };
}

[Authorize]
public class DocsProjectAppService(
    IRepository<ProjectDocsSpace, Guid> spaces,
    DocsGateway gateway,
    DocmostClient client,
    IProjectAccess projectAccess)
    : ApplicationService, IDocsProjectAppService
{
    public async Task<ProjectDocsDto> GetAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);
        var connection = await gateway.FindAsync();
        var space = await spaces.FirstOrDefaultAsync(item => item.ProjectId == projectId);
        return new ProjectDocsDto
        {
            Configured = connection?.EncryptedApiKey is not null,
            Space = space is null ? null : new DocmostSpaceDto { Id = space.DocmostSpaceId, Name = space.Name, Slug = space.Slug },
            SpaceUrl = space is null || connection is null ? null : DocmostClient.SpaceUrl(connection.BaseUrl, space.Slug),
            CanManage = await projectAccess.IsGrantedAsync(projectId, ProjectPermissions.Manage),
        };
    }

    public async Task<List<DocmostSpaceDto>> GetDocmostSpacesAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.Manage);
        return await gateway.CallAsync((access, _) => client.GetSpacesAsync(access));
    }

    public async Task<ProjectDocsDto> LinkAsync(Guid projectId, LinkDocsSpaceInput input)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.Manage);
        var found = (await gateway.CallAsync((access, _) => client.GetSpacesAsync(access)))
            .FirstOrDefault(item => item.Id == input.DocmostSpaceId)
            ?? throw new BusinessException(DocsErrors.SpaceNotFound);

        var space = await spaces.FirstOrDefaultAsync(item => item.ProjectId == projectId);
        if (space is null)
        {
            await spaces.InsertAsync(new ProjectDocsSpace(GuidGenerator.Create(), projectId, found.Id, found.Name, found.Slug), autoSave: true);
        }
        else
        {
            space.DocmostSpaceId = found.Id;
            space.Name = found.Name;
            space.Slug = found.Slug;
            await spaces.UpdateAsync(space, autoSave: true);
        }

        return await GetAsync(projectId);
    }

    public async Task UnlinkAsync(Guid projectId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.Manage);
        await spaces.DeleteAsync(item => item.ProjectId == projectId);
    }

    public async Task<List<DocsPageNodeDto>> GetPagesAsync(Guid projectId, string? parentPageId)
    {
        await projectAccess.CheckAsync(projectId, ProjectPermissions.ViewTickets);
        var space = await spaces.FirstOrDefaultAsync(item => item.ProjectId == projectId)
            ?? throw new BusinessException(DocsErrors.SpaceNotLinked);
        return await gateway.CallAsync(async (access, connection) =>
            (await client.GetPagesAsync(access, space.DocmostSpaceId, string.IsNullOrWhiteSpace(parentPageId) ? null : parentPageId))
                .Select(page => new DocsPageNodeDto
                {
                    Id = page.Id,
                    SlugId = page.SlugId,
                    Title = page.Title,
                    Icon = page.Icon,
                    HasChildren = page.HasChildren,
                    Url = DocmostClient.PageUrl(connection.BaseUrl, space.Slug, page.SlugId, page.Title),
                })
                .ToList());
    }
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
            Pages = [.. rows
                .GroupBy(item => item.PageId)
                .Select(group => group.OrderByDescending(item => item.Source).First())
                .OrderByDescending(item => item.UpdatedAt)
                .Select(ToDto)],
        };
    }

    public async Task<TicketDocPageDto> LinkAsync(Guid ticketId, LinkDocPageInput input)
    {
        var projectId = await tickets.GetProjectIdAsync(ticketId);
        await projectAccess.CheckAsync(projectId, ProjectPermissions.EditTickets);
        var connection = await gateway.FindAsync() ?? throw new BusinessException(DocsErrors.NotConfigured);
        var slugId = DocmostClient.SlugIdFromUrl(input.Url, connection.BaseUrl)
            ?? throw new BusinessException(DocsErrors.InvalidPageUrl);

        var found = await gateway.CallAsync((access, _) => client.FindPageAsync(access, slugId))
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
        if (link.Source == DocLinkSource.Mention)
        {
            throw new BusinessException(DocsErrors.MentionLink);
        }

        await links.DeleteAsync(link);
    }

    public async Task<TicketDocPageDto> CreatePageAsync(Guid ticketId)
    {
        var projectId = await tickets.GetProjectIdAsync(ticketId);
        await projectAccess.CheckAsync(projectId, ProjectPermissions.EditTickets);
        var space = await spaces.FirstOrDefaultAsync(item => item.ProjectId == projectId)
            ?? throw new BusinessException(DocsErrors.SpaceNotLinked);
        var card = await tickets.FindCardAsync(ticketId) ?? throw new EntityNotFoundException(typeof(TicketDocLink), ticketId);

        var title = $"{card.Key} {card.Title}";
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
            var page = await client.CreatePageAsync(access, space.DocmostSpaceId, title, content);
            return (DocmostClient.PageUrl(connection.BaseUrl, space.Slug, page.SlugId, page.Title), page);
        });

        var row = await links.InsertAsync(new TicketDocLink(
            GuidGenerator.Create(), ticketId, projectId, created.Id, DocLinkSource.Manual, created.Title, link, space.Name, clock.Now), autoSave: true);
        return ToDto(row);
    }

    private static TicketDocPageDto ToDto(TicketDocLink link) => new()
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
