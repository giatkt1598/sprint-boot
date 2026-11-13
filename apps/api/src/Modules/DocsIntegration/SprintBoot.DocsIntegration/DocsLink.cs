using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace SprintBoot.DocsIntegration;

/// <summary>A link one side started and the other has yet to authorize.</summary>
public class DocsLinkState
{
    public string DocmostUrl { get; set; } = null!;

    public Guid UserId { get; set; }
}

/// <summary>
/// Links Sprint Boot and Docmost in one go, from either side: whoever starts sends the browser to the other's
/// authorize page with a one-time state, and the authorizing side hands its credentials to the starting side's
/// server, which answers with its own. Nobody copies keys or secrets by hand.
/// </summary>
public class DocsLinker(
    IDistributedCache<DocsLinkState> states,
    IRepository<DocsConnection, Guid> connections,
    IRepository<ProjectDocsSpace, Guid> spaces,
    DocsGateway gateway,
    DocmostClient client,
    IStringEncryptionService encryption,
    IUnitOfWorkManager unitOfWork,
    IGuidGenerator guids) : ITransientDependency
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public async Task<string> StartAsync(string docmostUrl, Guid userId)
    {
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await states.SetAsync(state, new DocsLinkState { DocmostUrl = Normalize(docmostUrl), UserId = userId },
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime });
        return state;
    }

    public async Task<DocsLinkState> PeekAsync(string? state) =>
        (string.IsNullOrWhiteSpace(state) ? null : await states.GetAsync(state)) ?? throw new BusinessException(DocsErrors.LinkExpired);

    /// <summary>Used once: a second attempt with the same state is refused.</summary>
    public async Task<DocsLinkState> TakeAsync(string? state)
    {
        var found = await PeekAsync(state);
        await states.RemoveAsync(state!);
        return found;
    }

    public static string Normalize(string url) => url.Trim().TrimEnd('/');

    /// <summary>
    /// Gives the connection a new bridge secret at once, in its own transaction, because the other side checks it
    /// with a call back before this request ends.
    /// </summary>
    public async Task<string> NewSecretAsync(string docmostUrl)
    {
        var secret = $"sbd_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()}";
        using var uow = unitOfWork.Begin(requiresNew: true, isTransactional: true);
        var connection = await gateway.FindAsync()
            ?? await connections.InsertAsync(new DocsConnection(guids.Create(), Normalize(docmostUrl)), autoSave: true);
        connection.BaseUrl = Normalize(docmostUrl);
        connection.BridgeSecretHash = DocsGateway.HashSecret(secret);
        await connections.UpdateAsync(connection, autoSave: true);
        await uow.CompleteAsync();
        return secret;
    }

    /// <summary>Stores the key Docmost handed over and checks it; a key Docmost refuses fails the link.</summary>
    public async Task<DocsConnection> SaveKeyAsync(string docmostUrl, string apiKey)
    {
        var connection = await gateway.FindAsync()
            ?? await connections.InsertAsync(new DocsConnection(guids.Create(), Normalize(docmostUrl)), autoSave: true);
        connection.BaseUrl = Normalize(docmostUrl);
        connection.EncryptedApiKey = encryption.Encrypt(apiKey.Trim());
        await gateway.CheckAsync(connection);
        if (connection.Problem != DocsProblem.None)
        {
            throw new BusinessException(connection.Problem == DocsProblem.Unauthorized ? DocsErrors.Unauthorized : DocsErrors.Unavailable);
        }

        return connection;
    }

    /// <summary>Links a Sprint Boot space with a Docmost space the new key can read.</summary>
    public async Task MapAsync(Guid projectId, string docmostSpaceId)
    {
        var found = (await gateway.CallAsync((access, _) => client.GetSpacesAsync(access))).FirstOrDefault(item => item.Id == docmostSpaceId)
            ?? throw new BusinessException(DocsErrors.SpaceNotFound);
        var space = await spaces.FirstOrDefaultAsync(item => item.ProjectId == projectId);
        if (space is null)
        {
            await spaces.InsertAsync(new ProjectDocsSpace(guids.Create(), projectId, found.Id, found.Name, found.Slug), autoSave: true);
        }
        else
        {
            space.DocmostSpaceId = found.Id;
            space.Name = found.Name;
            space.Slug = found.Slug;
            await spaces.UpdateAsync(space, autoSave: true);
        }
    }

    public static bool SameHost(string a, string b) =>
        Uri.TryCreate(a, UriKind.Absolute, out var left) && Uri.TryCreate(b, UriKind.Absolute, out var right)
        && string.Equals(left.Authority, right.Authority, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Sprint Boot's authorize page, for a link Docmost started.</summary>
[Authorize(SprintBootPermissions.ManageDocs)]
public class DocsLinkAppService(DocsLinker linker, DocmostClient client, DocsGateway gateway, IProjectAccess projectAccess)
    : ApplicationService, IDocsLinkAppService
{
    public async Task<DocsLinkRequestDto> GetRequestAsync(string docmostUrl, string state)
    {
        var url = DocsLinker.Normalize(docmostUrl);
        List<DocmostSpaceDto> docmostSpaces;
        try
        {
            docmostSpaces = await client.GetLinkSpacesAsync(url, state);
        }
        catch (DocmostNotFoundException)
        {
            throw new BusinessException(DocsErrors.LinkExpired);
        }
        catch (Exception failure) when (failure is DocmostUnavailableException or DocmostAuthException)
        {
            throw DocmostFailures.ToBusiness(failure);
        }

        return new DocsLinkRequestDto { DocmostUrl = url, Spaces = await SpacesAsync(), DocmostSpaces = docmostSpaces };
    }

    public async Task<DocsLinkDoneDto> AuthorizeAsync(AuthorizeDocsLinkInput input)
    {
        var url = DocsLinker.Normalize(input.DocmostUrl);
        if (input.ProjectId is { } chosen)
        {
            await projectAccess.CheckAsync(chosen, ProjectPermissions.Manage);
        }

        var secret = await linker.NewSecretAsync(url);
        string apiKey;
        try
        {
            apiKey = await client.CompleteLinkAsync(url, input.State, gateway.PublicUrl, secret);
        }
        catch (DocmostNotFoundException)
        {
            throw new BusinessException(DocsErrors.LinkExpired);
        }
        catch (Exception failure) when (failure is DocmostUnavailableException or DocmostAuthException)
        {
            throw DocmostFailures.ToBusiness(failure);
        }

        await linker.SaveKeyAsync(url, apiKey);
        if (input.ProjectId is { } projectId && !string.IsNullOrWhiteSpace(input.DocmostSpaceId))
        {
            await linker.MapAsync(projectId, input.DocmostSpaceId);
        }

        return new DocsLinkDoneDto { ReturnUrl = $"{url}/settings/integrations" };
    }

    private async Task<List<DocsLinkSpaceDto>> SpacesAsync() =>
        [.. (await projectAccess.GetAllRefsAsync())
            .Where(space => !space.IsArchived)
            .OrderBy(space => space.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(space => new DocsLinkSpaceDto { Id = space.Id, Key = space.Key, Name = space.Name })];
}

public class DocsLinkRequestInput
{
    public string State { get; set; } = null!;
}

public class DocsLinkCompleteInput
{
    public string State { get; set; } = null!;

    public string DocmostUrl { get; set; } = null!;

    public string ApiKey { get; set; } = null!;

    /// <summary>The Sprint Boot space to link, by key, with the Docmost space below; both or neither.</summary>
    public string? SpaceKey { get; set; }

    public string? DocmostSpaceId { get; set; }
}

/// <summary>
/// Docmost's side of a link Sprint Boot started. Anonymous to MVC: the one-time state from Sprint Boot is the proof.
/// Actions return objects, so ABP turns refusals into the usual JSON errors.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/v1/docs-link")]
[AllowAnonymous]
public class DocsLinkController(DocsLinker linker, DocsGateway gateway, IProjectAccess projectAccess) : ControllerBase
{
    /// <summary>The spaces Docmost's authorize page offers to link.</summary>
    [HttpPost("request")]
    public async Task<ActionResult<object>> RequestAsync([FromBody] DocsLinkRequestInput input)
    {
        await linker.PeekAsync(input.State);
        var spaces = (await projectAccess.GetAllRefsAsync())
            .Where(space => !space.IsArchived)
            .OrderBy(space => space.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(space => new { space.Key, space.Name });
        return new { spaces, sprintBootUrl = gateway.PublicUrl };
    }

    [HttpPost("complete")]
    public async Task<ActionResult<object>> CompleteAsync([FromBody] DocsLinkCompleteInput input)
    {
        var state = await linker.TakeAsync(input.State);
        // The key must come from the Docmost the admin named, never from wherever the request claims.
        if (!DocsLinker.SameHost(input.DocmostUrl ?? string.Empty, state.DocmostUrl) || string.IsNullOrWhiteSpace(input.ApiKey))
        {
            throw new BusinessException(DocsErrors.LinkExpired);
        }

        // The secret commits on its own first: the key below holds the connection row until the request ends.
        var secret = await linker.NewSecretAsync(state.DocmostUrl);
        await linker.SaveKeyAsync(state.DocmostUrl, input.ApiKey);
        if (!string.IsNullOrWhiteSpace(input.SpaceKey) && !string.IsNullOrWhiteSpace(input.DocmostSpaceId)
            && await projectAccess.FindRefByKeyAsync(input.SpaceKey.Trim().ToUpperInvariant()) is { } space)
        {
            await linker.MapAsync(space.Id, input.DocmostSpaceId);
        }

        return new { bridgeSecret = secret, returnUrl = $"{gateway.PublicUrl}/admin/integrations?linked=docmost" };
    }
}
