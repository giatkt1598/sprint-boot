using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace SprintBoot.Identity;

public class PersonalToken : Entity<Guid>
{
    public Guid UserId { get; private set; }

    public string Name { get; private set; } = null!;

    public PersonalTokenKind Kind { get; private set; }

    public bool ReadOnly { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public string Prefix { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public DateTime? ExpiresAt { get; private set; }

    public DateTime? LastUsedAt { get; set; }

    protected PersonalToken()
    {
    }

    public PersonalToken(Guid id, Guid userId, string name, PersonalTokenKind kind, bool readOnly, string tokenHash, string prefix, DateTime createdAt, DateTime? expiresAt)
        : base(id)
    {
        UserId = userId;
        Name = name;
        Kind = kind;
        ReadOnly = readOnly;
        TokenHash = tokenHash;
        Prefix = prefix;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }
}

public static class PersonalTokenSecret
{
    public const string Marker = "sb_";

    private const int PrefixLength = 12;

    public static (string Token, string Hash, string Prefix) Generate(PersonalTokenKind kind)
    {
        var random = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var token = $"{Marker}{(kind == PersonalTokenKind.Mcp ? "mcp" : "api")}_{random}";
        return (token, Hash(token), token[..PrefixLength]);
    }

    /// <summary>Tokens are long and random, so a plain hash is enough and lookups stay fast.</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

[Authorize]
public class PersonalTokenAppService(IRepository<PersonalToken, Guid> tokens, ICurrentUser currentUser)
    : ApplicationService, IPersonalTokenAppService
{
    public async Task<List<PersonalTokenDto>> GetListAsync()
    {
        var userId = currentUser.GetId();
        var list = await tokens.GetListAsync(token => token.UserId == userId);
        return [.. list.OrderByDescending(token => token.CreatedAt).Select(ToDto)];
    }

    public async Task<CreatedPersonalTokenDto> CreateAsync(CreatePersonalTokenInput input)
    {
        var (secret, hash, prefix) = PersonalTokenSecret.Generate(input.Kind);
        var now = Clock.Now;
        var expiresAt = input.ExpiresInDays is { } days ? now.AddDays(days) : (DateTime?)null;
        var token = await tokens.InsertAsync(
            new PersonalToken(GuidGenerator.Create(), currentUser.GetId(), input.Name.Trim(), input.Kind, input.ReadOnly, hash, prefix, now, expiresAt),
            autoSave: true);

        return new CreatedPersonalTokenDto
        {
            Id = token.Id,
            Name = token.Name,
            Kind = token.Kind,
            ReadOnly = token.ReadOnly,
            Prefix = token.Prefix,
            CreatedAt = token.CreatedAt,
            ExpiresAt = token.ExpiresAt,
            LastUsedAt = null,
            Token = secret,
        };
    }

    public async Task DeleteAsync(Guid id)
    {
        var token = await tokens.GetAsync(id);
        if (token.UserId != currentUser.GetId())
        {
            throw new EntityNotFoundException(typeof(PersonalToken), id);
        }

        await tokens.DeleteAsync(token);
    }

    private static PersonalTokenDto ToDto(PersonalToken token) => new()
    {
        Id = token.Id,
        Name = token.Name,
        Kind = token.Kind,
        ReadOnly = token.ReadOnly,
        Prefix = token.Prefix,
        CreatedAt = token.CreatedAt,
        ExpiresAt = token.ExpiresAt,
        LastUsedAt = token.LastUsedAt,
    };
}

/// <summary>Signs a request in as the token's owner, with exactly that person's permissions.</summary>
public class PersonalTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IRepository<PersonalToken, Guid> tokens,
    IdentityUserManager users,
    IUnitOfWorkManager unitOfWork,
    IClock clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PersonalToken";

    private static readonly TimeSpan UseRecordInterval = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var hash = PersonalTokenSecret.Hash(header["Bearer ".Length..].Trim());

        // Authentication runs before the request's own unit of work starts.
        using var scope = unitOfWork.Begin(requiresNew: true);
        var token = await tokens.FirstOrDefaultAsync(item => item.TokenHash == hash);
        var now = clock.Now;
        if (token is null || token.ExpiresAt <= now)
        {
            return AuthenticateResult.Fail("Invalid token.");
        }

        var user = await users.FindByIdAsync(token.UserId.ToString());
        if (user is null || !user.IsActive || await users.IsLockedOutAsync(user))
        {
            return AuthenticateResult.Fail("Invalid token.");
        }

        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, user.Id.ToString()),
            new(AbpClaimTypes.UserName, user.UserName),
            new(AbpClaimTypes.Email, user.Email),
            new(PersonalTokenClaims.Kind, token.Kind.ToString()),
            new(PersonalTokenClaims.ReadOnly, token.ReadOnly.ToString(CultureInfo.InvariantCulture)),
        };
        claims.AddRange((await users.GetRolesAsync(user)).Select(role => new Claim(AbpClaimTypes.Role, role)));

        if (token.LastUsedAt is null || now - token.LastUsedAt > UseRecordInterval)
        {
            token.LastUsedAt = now;
            await tokens.UpdateAsync(token);
        }

        await scope.CompleteAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}

/// <summary>Keeps each token to its own surface: API tokens to the API, MCP tokens to /mcp.</summary>
public static class PersonalTokenGate
{
    public const string McpPath = "/mcp";

    private static readonly string[] OffLimits = ["/api/v1/personal-token", "/api/v1/auth"];

    public static IApplicationBuilder UsePersonalTokenGate(this IApplicationBuilder app) => app.Use((http, next) =>
    {
        var path = http.Request.Path;
        var kind = http.User.FindFirstValue(PersonalTokenClaims.Kind);
        var mcpPath = path.StartsWithSegments(McpPath);

        if (kind is null)
        {
            return mcpPath ? Refuse(http, StatusCodes.Status401Unauthorized) : next();
        }

        var allowed = kind == nameof(PersonalTokenKind.Mcp)
            ? mcpPath
            : path.StartsWithSegments("/api")
                && !OffLimits.Any(item => path.StartsWithSegments(item))
                && (!IsReadOnly(http.User) || HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method));

        return allowed ? next() : Refuse(http, StatusCodes.Status403Forbidden);
    });

    public static bool IsReadOnly(ClaimsPrincipal user) =>
        string.Equals(user.FindFirstValue(PersonalTokenClaims.ReadOnly), bool.TrueString, StringComparison.OrdinalIgnoreCase);

    private static Task Refuse(HttpContext http, int status)
    {
        http.Response.StatusCode = status;
        return Task.CompletedTask;
    }
}
