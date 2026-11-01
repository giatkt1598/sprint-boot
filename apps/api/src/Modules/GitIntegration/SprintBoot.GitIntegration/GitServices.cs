using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SprintBoot.Issues;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace SprintBoot.GitIntegration;

public class GitCredentials
{
    public string? Token { get; set; }

    public string? RefreshToken { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }
}

public static partial class IssueKeyScanner
{
    /// <summary>Only keys of known projects count. Branch names may be lower case; prose may not.</summary>
    public static HashSet<string> Scan(string? text, IReadOnlyCollection<string> projectKeys, bool ignoreCase)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text) || projectKeys.Count == 0)
        {
            return found;
        }

        var keys = string.Join('|', projectKeys.Select(Regex.Escape));
        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        foreach (Match match in Regex.Matches(text, $"(?<![A-Za-z0-9])({keys})-([1-9][0-9]*)(?![A-Za-z0-9])", options, TimeSpan.FromSeconds(1)))
        {
            found.Add($"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}");
        }

        return found;
    }

    public static string BranchName(string? template, string key, string title)
    {
        var slug = Slug(title);

        // {key} and {slug} are the older spellings, kept for templates already saved.
        var name = (string.IsNullOrWhiteSpace(template) ? "feature/{issue_key}-{issue_title}" : template)
            .Replace("{issue_key}", key, StringComparison.Ordinal)
            .Replace("{issue_title}", slug, StringComparison.Ordinal)
            .Replace("{key}", key, StringComparison.Ordinal)
            .Replace("{slug}", slug, StringComparison.Ordinal);
        return name.TrimEnd('-', '/');
    }

    public static string Slug(string title)
    {
        var plain = new string(title.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD)
            .Where(letter => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(letter) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
        var slug = NotSlug().Replace(plain.ToLowerInvariant(), "-").Trim('-');
        return slug.Length <= 40 ? slug : slug[..40].TrimEnd('-');
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlug();
}

/// <summary>Turns a stored connection into a working credential.</summary>
public class GitAccessProvider(
    IEnumerable<IGitProvider> providers,
    IStringEncryptionService encryption,
    IHttpClientFactory clients,
    IConfiguration configuration,
    IClock clock) : ITransientDependency
{
    private static readonly ConcurrentDictionary<long, (string Token, DateTime Expires)> InstallationTokens = new();

    public IGitProvider Provider(GitProviderType type) => providers.Single(provider => provider.Type == type);

    public GitCredentials Read(GitConnection connection) =>
        connection.EncryptedCredentials is null
            ? new GitCredentials()
            : JsonSerializer.Deserialize<GitCredentials>(encryption.Decrypt(connection.EncryptedCredentials)!)!;

    public void Write(GitConnection connection, GitCredentials credentials) =>
        connection.EncryptedCredentials = encryption.Encrypt(JsonSerializer.Serialize(credentials));

    public async Task<GitAccess> GetAsync(GitConnection connection)
    {
        if (connection.AuthType == GitAuthType.GitHubApp)
        {
            return new GitAccess(connection.BaseUrl, await InstallationTokenAsync(connection));
        }

        var credentials = Read(connection);
        if (connection.AuthType == GitAuthType.OAuth2 && credentials.ExpiresAt <= clock.Now.AddMinutes(1))
        {
            await RefreshAsync(connection, credentials);
        }

        return new GitAccess(connection.BaseUrl, credentials.Token ?? throw new GitAuthException("No credential is stored."));
    }

    public async Task RedeemCodeAsync(GitConnection connection, string code, string redirectUri)
    {
        var credentials = Read(connection);
        await ExchangeAsync(connection, credentials, new
        {
            grant_type = "authorization_code",
            client_id = credentials.ClientId,
            client_secret = credentials.ClientSecret,
            code,
            redirect_uri = redirectUri,
        });
    }

    private Task RefreshAsync(GitConnection connection, GitCredentials credentials) =>
        ExchangeAsync(connection, credentials, new
        {
            grant_type = "refresh_token",
            client_id = credentials.ClientId,
            client_secret = credentials.ClientSecret,
            refresh_token = credentials.RefreshToken,
        });

    private async Task ExchangeAsync(GitConnection connection, GitCredentials credentials, object body)
    {
        var client = clients.CreateClient(GitHttp.ClientName);
        var response = await GitHttp.SendAsync(client, HttpMethod.Post, $"{connection.BaseUrl}/login/oauth/access_token", null, body);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitAuthException("The provider refused the OAuth2 exchange.");
        }

        var token = await GitHttp.ReadAsync(response);
        credentials.Token = token.GetProperty("access_token").GetString();
        credentials.RefreshToken = GitPayloads.Text(token, "refresh_token") ?? credentials.RefreshToken;
        credentials.ExpiresAt = clock.Now.AddSeconds(token.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600);
        Write(connection, credentials);
    }

    private async Task<string> InstallationTokenAsync(GitConnection connection)
    {
        var installation = connection.InstallationId ?? throw new GitAuthException("The GitHub App is not installed.");
        if (InstallationTokens.TryGetValue(installation, out var cached) && cached.Expires > DateTime.UtcNow.AddMinutes(1))
        {
            return cached.Token;
        }

        var apiUrl = (configuration["Git:GitHub:ApiUrl"] ?? "https://api.github.com").TrimEnd('/');
        var response = await GitHttp.SendAsync(
            clients.CreateClient(GitHttp.ClientName),
            HttpMethod.Post,
            $"{apiUrl}/app/installations/{installation}/access_tokens",
            new AuthenticationHeaderValue("Bearer", AppJwt()));
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
        {
            throw new GitAuthException("The GitHub App installation is gone.");
        }

        var body = await GitHttp.ReadAsync(response);
        var token = body.GetProperty("token").GetString()!;
        InstallationTokens[installation] = (token, body.GetProperty("expires_at").GetDateTime().ToUniversalTime());
        return token;
    }

    private string AppJwt()
    {
        var appId = configuration["Git:GitHub:AppId"];
        var key = configuration["Git:GitHub:PrivateKey"];
        if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(key))
        {
            throw new BusinessException(GitErrors.GitHubNotConfigured);
        }

        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Backdated because GitHub rejects tokens from a clock slightly ahead of its own.
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new { iat = now - 60, exp = now + 540, iss = appId }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(key.Replace("\\n", "\n", StringComparison.Ordinal));
        var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{claims}.{Encode(signature)}";
    }
}

public class GitSyncService(
    IRepository<GitConnection, Guid> connections,
    IRepository<GitRepository, Guid> repositories,
    GitAccessProvider access,
    IStringEncryptionService encryption,
    IConfiguration configuration,
    IGuidGenerator guids,
    IClock clock,
    ILogger<GitSyncService> logger) : ITransientDependency
{
    public string PublicUrl => (configuration["Git:PublicUrl"] ?? configuration["Auth:SpaBaseUrl"] ?? string.Empty).TrimEnd('/');

    public string WebhookUrl(GitRepository repository) => $"{PublicUrl}/api/v1/git/webhooks/gitea/{repository.Id}";

    /// <summary>A failure is recorded on the connection for the admin; it never throws.</summary>
    public async Task SyncAsync(GitConnection connection)
    {
        var provider = access.Provider(connection.ProviderType);
        try
        {
            var credential = await access.GetAsync(connection);
            var remote = (await provider.ListRepositoriesAsync(credential)).Where(info => connection.Covers(info.FullName)).ToList();
            var stored = await repositories.GetListAsync(item => item.ConnectionId == connection.Id);
            foreach (var info in remote)
            {
                var repository = stored.FirstOrDefault(item => item.ExternalId == info.ExternalId);
                if (repository is null)
                {
                    repository = new GitRepository(guids.Create(), connection.Id, info.ExternalId);
                    Apply(repository, info);
                    await repositories.InsertAsync(repository, autoSave: true);
                }
                else
                {
                    Apply(repository, info);
                    await repositories.UpdateAsync(repository);
                }

                if (!provider.HasConnectionWideWebhook && repository.WebhookState != GitWebhookState.Active)
                {
                    await RegisterAsync(provider, credential, repository);
                }
            }

            // Repositories that lost access stay, because their links do.
            var ids = remote.Select(info => info.ExternalId).ToHashSet();
            foreach (var gone in stored.Where(item => item.IsAccessible && !ids.Contains(item.ExternalId)))
            {
                gone.IsAccessible = false;
                await repositories.UpdateAsync(gone);
            }

            connection.Status = GitConnectionStatus.Connected;
            connection.Problem = GitConnectionProblem.None;
            connection.LastSyncAt = clock.Now;
        }
        catch (GitAuthException failure)
        {
            logger.LogWarning("Git connection {ConnectionId} lost access: {Reason}", connection.Id, failure.Message);
            connection.Status = GitConnectionStatus.NeedsAttention;
            connection.Problem = GitConnectionProblem.TokenRevoked;
        }
        catch (GitProviderException failure)
        {
            logger.LogWarning("Git connection {ConnectionId} failed to sync: {Reason}", connection.Id, failure.Message);
            connection.Status = GitConnectionStatus.NeedsAttention;
            connection.Problem = GitConnectionProblem.Unreachable;
        }

        await connections.UpdateAsync(connection);
    }

    public static void Apply(GitRepository repository, GitRepoInfo info)
    {
        repository.FullName = info.FullName;
        repository.DefaultBranch = info.DefaultBranch;
        repository.IsPrivate = info.IsPrivate;
        repository.WebUrl = info.WebUrl;
        repository.IsAccessible = true;
    }

    private async Task RegisterAsync(IGitProvider provider, GitAccess credential, GitRepository repository)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        repository.EncryptedWebhookSecret ??= encryption.Encrypt(secret);
        try
        {
            repository.ExternalHookId = await provider.RegisterWebhookAsync(
                credential, repository.FullName, WebhookUrl(repository), encryption.Decrypt(repository.EncryptedWebhookSecret)!);
            repository.WebhookState = GitWebhookState.Active;
        }
        catch (GitProviderException failure)
        {
            // Usually a token without admin rights on this repository; the admin adds the hook by hand.
            logger.LogInformation("Webhook not registered on {Repository}: {Reason}", repository.FullName, failure.Message);
            repository.WebhookState = GitWebhookState.Failed;
        }

        await repositories.UpdateAsync(repository);
    }
}

public class GitSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int FifteenMinutes = 15 * 60 * 1000;

    public GitSyncWorker(AbpAsyncTimer timer, IServiceScopeFactory scopeFactory)
        : base(timer, scopeFactory)
    {
        timer.Period = FifteenMinutes;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        await using var handle = await services.GetRequiredService<IAbpDistributedLock>().TryAcquireAsync(nameof(GitSyncWorker));
        if (handle is null)
        {
            return;
        }

        var connections = services.GetRequiredService<IRepository<GitConnection, Guid>>();
        var unitOfWork = services.GetRequiredService<IUnitOfWorkManager>();
        List<Guid> ids;
        using (var uow = unitOfWork.Begin(requiresNew: true))
        {
            ids = [.. (await connections.GetListAsync(item => item.Status != GitConnectionStatus.Pending)).Select(item => item.Id)];
            await uow.CompleteAsync();
        }

        foreach (var id in ids)
        {
            using var uow = unitOfWork.Begin(requiresNew: true, isTransactional: true);
            await services.GetRequiredService<GitSyncService>().SyncAsync(await connections.GetAsync(id));
            await uow.CompleteAsync();
        }
    }
}

/// <summary>Accepts deliveries: nothing is written before the signature checks out.</summary>
public class GitWebhookIntake(
    IRepository<GitRepository, Guid> repositories,
    IRepository<GitConnection, Guid> connections,
    IRepository<GitWebhookDelivery, Guid> deliveries,
    IDistributedEventBus eventBus,
    IStringEncryptionService encryption,
    IConfiguration configuration,
    IUnitOfWorkManager unitOfWork,
    IGuidGenerator guids,
    IClock clock) : ITransientDependency
{
    public async Task<bool> AcceptGiteaAsync(Guid repositoryId, string? deliveryId, string? eventName, string? signature, string body)
    {
        var repository = await repositories.FindAsync(repositoryId);
        var secret = repository?.EncryptedWebhookSecret is null ? null : encryption.Decrypt(repository.EncryptedWebhookSecret);
        if (secret is null || !GitHttp.SignatureMatches(body, secret, signature))
        {
            return false;
        }

        await StoreAsync(GitProviderType.Gitea, repository!.ConnectionId, deliveryId, eventName, body);
        return true;
    }

    public async Task<bool> AcceptGitHubAsync(string? deliveryId, string? eventName, string? signature, string body)
    {
        const string prefix = "sha256=";
        var secret = configuration["Git:GitHub:WebhookSecret"];
        if (string.IsNullOrEmpty(secret) || signature?.StartsWith(prefix, StringComparison.Ordinal) != true
            || !GitHttp.SignatureMatches(body, secret, signature[prefix.Length..]))
        {
            return false;
        }

        using var document = JsonDocument.Parse(body);
        var installation = document.RootElement.TryGetProperty("installation", out var element)
            ? element.GetProperty("id").GetInt64()
            : (long?)null;
        var connection = await connections.FirstOrDefaultAsync(item => item.InstallationId == installation);
        await StoreAsync(GitProviderType.GitHub, connection?.Id, deliveryId, eventName, body);
        return true;
    }

    private async Task StoreAsync(GitProviderType provider, Guid? connectionId, string? deliveryId, string? eventName, string body)
    {
        if (string.IsNullOrEmpty(deliveryId) || string.IsNullOrEmpty(eventName))
        {
            return;
        }

        try
        {
            using var uow = unitOfWork.Begin(requiresNew: true, isTransactional: true);
            if (await deliveries.AnyAsync(item => item.Provider == provider && item.DeliveryId == deliveryId))
            {
                return;
            }

            var delivery = new GitWebhookDelivery(guids.Create(), deliveryId, provider, connectionId, eventName, body, clock.Now);
            await deliveries.InsertAsync(delivery, autoSave: true);
            await eventBus.PublishAsync(new GitWebhookReceivedEto { DeliveryId = delivery.Id }, useOutbox: true);
            await uow.CompleteAsync();
        }
        catch (DbUpdateException)
        {
            // The same delivery arrived twice at once; the unique index let one through.
        }
    }
}

public class GitWebhookHandler(
    IRepository<GitWebhookDelivery, Guid> deliveries,
    GitAccessProvider access,
    GitEventProcessor processor,
    IClock clock) : IDistributedEventHandler<GitWebhookReceivedEto>, ITransientDependency
{
    [UnitOfWork]
    public virtual async Task HandleEventAsync(GitWebhookReceivedEto eventData)
    {
        var delivery = await deliveries.FindAsync(eventData.DeliveryId);
        if (delivery is null || delivery.ProcessedAt is not null || delivery.ConnectionId is null)
        {
            return;
        }

        var parsed = access.Provider(delivery.Provider).Parse(delivery.EventName, delivery.Payload);
        if (parsed is not null)
        {
            await processor.ApplyAsync(delivery.ConnectionId.Value, delivery.Provider, parsed);
        }

        delivery.ProcessedAt = clock.Now;
        await deliveries.UpdateAsync(delivery);
    }
}

/// <summary>
/// The fallback for webhooks that never arrive. It reads a repository's recent work and feeds it
/// through the same processor, which already ignores what it has seen and links every issue of the space.
/// </summary>
public class GitPoller(
    IRepository<GitConnection, Guid> connections,
    IRepository<GitRepository, Guid> repositories,
    IRepository<GitBranch, Guid> branches,
    GitAccessProvider access,
    GitEventProcessor processor,
    IProjectAccess projects,
    IUnitOfWorkManager unitOfWork,
    ILogger<GitPoller> logger) : ITransientDependency
{
    public async Task PollAsync(Guid repositoryId)
    {
        try
        {
            using var uow = unitOfWork.Begin(requiresNew: true, isTransactional: true);
            await PollRepositoryAsync(repositoryId);
            await uow.CompleteAsync();
        }
        catch (GitAuthException failure)
        {
            logger.LogWarning("Polling repository {RepositoryId} lost access: {Reason}", repositoryId, failure.Message);
            await FlagConnectionAsync(repositoryId);
        }
        catch (GitProviderException failure)
        {
            logger.LogInformation("Polling repository {RepositoryId} failed: {Reason}", repositoryId, failure.Message);
        }
        catch (DbUpdateException failure)
        {
            // A webhook delivered the same thing at the same moment; the next poll settles it.
            logger.LogInformation(failure, "Polling repository {RepositoryId} overlapped a webhook", repositoryId);
        }
    }

    private async Task PollRepositoryAsync(Guid repositoryId)
    {
        var repository = await repositories.FindAsync(repositoryId);
        var connection = repository is null ? null : await connections.FindAsync(repository.ConnectionId);
        if (repository is not { IsAccessible: true } || connection?.ProjectId is not { } projectId || !connection.Covers(repository.FullName))
        {
            return;
        }

        var type = connection.ProviderType;
        var provider = access.Provider(type);
        var credential = await access.GetAsync(connection);
        var info = new GitRepoInfo(repository.ExternalId, repository.FullName, repository.DefaultBranch, repository.IsPrivate, repository.WebUrl);
        var keys = new[] { (await projects.GetRefAsync(projectId)).Key };
        Task ApplyAsync(GitEvent item) => processor.ApplyAsync(connection.Id, type, item, fromWebhook: false);

        var names = await provider.ListBranchesAsync(credential, repository.FullName);
        var main = await provider.ListCommitsAsync(credential, repository.FullName, repository.DefaultBranch);
        await ApplyAsync(new GitPushEvent(info, repository.DefaultBranch, main));
        var shas = main.Take(1).Select(commit => commit.Sha).ToList();
        var inherited = main.Select(commit => commit.Sha).ToHashSet();

        foreach (var name in names.Where(name => IssueKeyScanner.Scan(name, keys, ignoreCase: true).Count > 0))
        {
            await ApplyAsync(new GitBranchEvent(info, name, Deleted: false));

            // A branch lists the history it grew from too, which is not work on this issue.
            var commits = (await provider.ListCommitsAsync(credential, repository.FullName, name)).Where(commit => !inherited.Contains(commit.Sha)).ToList();
            await ApplyAsync(new GitPushEvent(info, name, commits));
            shas.InsertRange(0, commits.Take(1).Select(commit => commit.Sha));
        }

        var pulls = await provider.ListRecentPullRequestsAsync(credential, info);
        foreach (var pull in pulls)
        {
            await ApplyAsync(pull);
        }

        var openHeads = pulls.Where(pull => pull.State == GitPullRequestState.Open).Select(pull => pull.HeadSha);
        foreach (var sha in openHeads.Concat(shas).Distinct().Take(GitPolling.StatusLimit))
        {
            foreach (var status in await provider.ListStatusesAsync(credential, info, sha))
            {
                await ApplyAsync(status);
            }
        }

        // A full page may have been cut off, and dropping a branch on that guess would lose its links.
        if (names.Count < provider.BranchPageSize)
        {
            var stored = await branches.GetListAsync(item => item.RepositoryId == repository.Id);
            foreach (var gone in stored.Where(item => !names.Contains(item.Name)))
            {
                await ApplyAsync(new GitBranchEvent(info, gone.Name, Deleted: true));
            }
        }
    }

    private async Task FlagConnectionAsync(Guid repositoryId)
    {
        using var uow = unitOfWork.Begin(requiresNew: true);
        var repository = await repositories.GetAsync(repositoryId);
        var connection = await connections.GetAsync(repository.ConnectionId);
        connection.Status = GitConnectionStatus.NeedsAttention;
        connection.Problem = GitConnectionProblem.TokenRevoked;
        await connections.UpdateAsync(connection);
        await uow.CompleteAsync();
    }
}

public class GitPollHandler(GitPoller poller) : IDistributedEventHandler<GitPollRequestedEto>, ITransientDependency
{
    public Task HandleEventAsync(GitPollRequestedEto eventData) => poller.PollAsync(eventData.RepositoryId);
}

/// <summary>Provider-neutral: every test of this class holds for both GitHub and Gitea.</summary>
public class GitEventProcessor(
    IRepository<GitConnection, Guid> connections,
    IRepository<GitRepository, Guid> repositories,
    IRepository<GitBranch, Guid> branches,
    IRepository<GitCommit, Guid> commits,
    IRepository<GitPullRequest, Guid> pullRequests,
    IRepository<CiStatus, Guid> statuses,
    IRepository<IssueGitLink, Guid> links,
    IProjectAccess projects,
    IIssueKeyResolver resolver,
    IGuidGenerator guids,
    IClock clock) : ITransientDependency
{
    /// <param name="fromWebhook">False when we went looking: that says nothing about the webhook working.</param>
    public async Task ApplyAsync(Guid connectionId, GitProviderType provider, GitEvent gitEvent, bool fromWebhook = true)
    {
        var connection = await connections.GetAsync(connectionId);

        // Without a space there is nobody's issues to link to.
        if (connection.ProjectId is not { } projectId || !connection.Covers(gitEvent.Repository.FullName))
        {
            return;
        }

        var repository = await RepositoryAsync(connectionId, gitEvent.Repository, fromWebhook);

        // A connection serves its own space, so other spaces' keys are just text.
        List<string> projectKeys = [(await projects.GetRefAsync(projectId)).Key];
        switch (gitEvent)
        {
            case GitPushEvent push:
                await ApplyPushAsync(repository, push, projectKeys);
                break;
            case GitBranchEvent { Deleted: true } deleted:
                await RemoveBranchAsync(repository, deleted.Branch);
                break;
            case GitBranchEvent created:
                await LinkBranchAsync(repository, created.Branch, projectKeys);
                break;
            case GitPullRequestEvent pull:
                await ApplyPullRequestAsync(repository, pull, projectKeys);
                break;
            case GitStatusEvent status:
                await ApplyStatusAsync(repository, status);
                break;
        }
    }

    private async Task<GitRepository> RepositoryAsync(Guid connectionId, GitRepoInfo info, bool fromWebhook)
    {
        var repository = await repositories.FirstOrDefaultAsync(item => item.ConnectionId == connectionId && item.ExternalId == info.ExternalId);
        if (repository is null)
        {
            repository = new GitRepository(guids.Create(), connectionId, info.ExternalId);
            GitSyncService.Apply(repository, info);
            if (fromWebhook)
            {
                repository.WebhookState = GitWebhookState.Active;
                repository.LastEventAt = clock.Now;
            }

            return await repositories.InsertAsync(repository, autoSave: true);
        }

        // Repositories are matched by external id, so a rename just updates the name.
        GitSyncService.Apply(repository, info);
        if (fromWebhook)
        {
            repository.WebhookState = GitWebhookState.Active;
            repository.LastEventAt = clock.Now;
        }

        return await repositories.UpdateAsync(repository);
    }

    private async Task<Dictionary<string, IssueRef>> ResolveAsync(IEnumerable<string> keys) =>
        await resolver.ResolveAsync([.. keys.Distinct()]);

    private async Task ApplyPushAsync(GitRepository repository, GitPushEvent push, List<string> projectKeys)
    {
        var branchKeys = IssueKeyScanner.Scan(push.Branch, projectKeys, ignoreCase: true);
        var scanned = push.Commits
            .Select(commit => (Commit: commit, Keys: IssueKeyScanner.Scan(commit.Message, projectKeys, ignoreCase: false).Union(branchKeys).ToList()))
            .Where(item => item.Keys.Count > 0)
            .ToList();
        var issues = await ResolveAsync(scanned.SelectMany(item => item.Keys).Union(branchKeys));

        foreach (var key in branchKeys.Where(issues.ContainsKey))
        {
            await LinkBranchAsync(repository, push.Branch, projectKeys);
            break;
        }

        foreach (var (commit, keys) in scanned)
        {
            var targets = keys.Where(issues.ContainsKey).Select(key => issues[key]).ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            var stored = await commits.FirstOrDefaultAsync(item => item.RepositoryId == repository.Id && item.Sha == commit.Sha)
                ?? await commits.InsertAsync(
                    new GitCommit(guids.Create(), repository.Id, commit.Sha, commit.Message, commit.AuthorName, commit.CommittedAt, commit.Url),
                    autoSave: true);
            foreach (var target in targets)
            {
                await LinkAsync(target, GitLinkTarget.Commit, stored.Id);
            }
        }
    }

    private async Task LinkBranchAsync(GitRepository repository, string name, List<string> projectKeys)
    {
        var issues = await ResolveAsync(IssueKeyScanner.Scan(name, projectKeys, ignoreCase: true));
        if (issues.Count == 0)
        {
            return;
        }

        var branch = await branches.FirstOrDefaultAsync(item => item.RepositoryId == repository.Id && item.Name == name)
            ?? await branches.InsertAsync(new GitBranch(guids.Create(), repository.Id, name), autoSave: true);
        foreach (var issue in issues.Values)
        {
            await LinkAsync(issue, GitLinkTarget.Branch, branch.Id);
        }
    }

    private async Task RemoveBranchAsync(GitRepository repository, string name)
    {
        var branch = await branches.FirstOrDefaultAsync(item => item.RepositoryId == repository.Id && item.Name == name);
        if (branch is not null)
        {
            await links.DeleteAsync(item => item.TargetType == GitLinkTarget.Branch && item.TargetId == branch.Id);
            await branches.DeleteAsync(branch);
        }
    }

    private async Task ApplyPullRequestAsync(GitRepository repository, GitPullRequestEvent pull, List<string> projectKeys)
    {
        var keys = IssueKeyScanner.Scan($"{pull.Title}\n{pull.Body}", projectKeys, ignoreCase: false)
            .Union(IssueKeyScanner.Scan(pull.HeadBranch, projectKeys, ignoreCase: true));
        var issues = await ResolveAsync(keys);
        var stored = await pullRequests.FirstOrDefaultAsync(item => item.RepositoryId == repository.Id && item.Number == pull.Number);
        if (stored is null && issues.Count == 0)
        {
            return;
        }

        if (stored is null)
        {
            stored = new GitPullRequest(guids.Create(), repository.Id, pull.Number);
            Apply(stored, pull);
            await pullRequests.InsertAsync(stored, autoSave: true);
        }
        else
        {
            Apply(stored, pull);
            await pullRequests.UpdateAsync(stored);
        }

        foreach (var issue in issues.Values)
        {
            await LinkAsync(issue, GitLinkTarget.PullRequest, stored.Id);
        }
    }

    private static void Apply(GitPullRequest stored, GitPullRequestEvent pull)
    {
        stored.Title = pull.Title;
        stored.State = pull.State;
        stored.Url = pull.Url;
        stored.HeadSha = pull.HeadSha;
    }

    private async Task ApplyStatusAsync(GitRepository repository, GitStatusEvent status)
    {
        var stored = await statuses.FirstOrDefaultAsync(item => item.RepositoryId == repository.Id && item.Sha == status.Sha && item.Name == status.Name);
        if (stored is null)
        {
            stored = new CiStatus(guids.Create(), repository.Id, status.Sha, status.Name);
            await statuses.InsertAsync(stored, autoSave: true);
        }

        stored.State = status.State;
        stored.Url = status.Url;
        stored.UpdatedAt = clock.Now;
        await statuses.UpdateAsync(stored);
    }

    public async Task LinkAsync(IssueRef issue, GitLinkTarget type, Guid targetId)
    {
        if (!await links.AnyAsync(item => item.IssueId == issue.IssueId && item.TargetType == type && item.TargetId == targetId))
        {
            await links.InsertAsync(new IssueGitLink(guids.Create(), issue.IssueId, issue.ProjectId, type, targetId), autoSave: true);
        }
    }
}
