using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace SprintBoot.IntegrationTests;

/// <summary>Plays the role of the SPA: CSRF, cookie login, authorize with PKCE, token exchange.</summary>
public sealed class AuthClient(ApiFixture fixture)
{
    public const string RedirectUri = "http://localhost:5173/auth/callback";

    public HttpClient Http { get; } = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    public Guid UserId { get; init; }

    public string? AccessToken { get; private set; }

    public string? RefreshToken { get; private set; }

    public async Task<string> GetCsrfTokenAsync(CancellationToken token)
    {
        var response = await Http.GetAsync("/api/v1/auth/csrf", token);
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").First(value => value.StartsWith("XSRF-TOKEN="));
        return WebUtility.UrlDecode(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
    }

    public async Task<HttpResponseMessage> PostLoginAsync(string email, string password, string? csrf, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        if (csrf is not null)
        {
            request.Headers.Add("RequestVerificationToken", csrf);
        }

        return await Http.SendAsync(request, token);
    }

    public async Task SignInAsync(string email, string password, CancellationToken token)
    {
        var csrf = await GetCsrfTokenAsync(token);
        var login = await PostLoginAsync(email, password, csrf, token);
        login.StatusCode.ShouldBe(HttpStatusCode.NoContent, await login.Content.ReadAsStringAsync(token));
        await AuthorizeAsync(token);
    }

    /// <summary>After any cookie sign-in: the SPA's authorize request and token exchange.</summary>
    public async Task AuthorizeAsync(CancellationToken token)
    {
        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = "sprint-boot-web";
        query["redirect_uri"] = RedirectUri;
        query["response_type"] = "code";
        query["scope"] = "openid profile email offline_access sprint-boot-api";
        query["code_challenge"] = challenge;
        query["code_challenge_method"] = "S256";
        query["state"] = "test-state";

        var authorize = await Http.GetAsync($"/connect/authorize?{query}", token);
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect, await authorize.Content.ReadAsStringAsync(token));
        var location = authorize.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).ShouldBe(RedirectUri);
        var code = HttpUtility.ParseQueryString(location.Query)["code"];
        code.ShouldNotBeNullOrEmpty();

        await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "sprint-boot-web",
            ["redirect_uri"] = RedirectUri,
            ["code"] = code,
            ["code_verifier"] = verifier,
        }, token);
    }

    public Task RefreshAsync(CancellationToken token) => ExchangeAsync(new Dictionary<string, string>
    {
        ["grant_type"] = "refresh_token",
        ["client_id"] = "sprint-boot-web",
        ["refresh_token"] = RefreshToken!,
    }, token);

    public async Task<HttpResponseMessage> PostFormAsync(string path, Dictionary<string, string> form, CancellationToken token) =>
        await Http.PostAsync(path, new FormUrlEncodedContent(form), token);

    public async Task<HttpResponseMessage> PostJsonAsync(string path, object body, string csrf, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", csrf);
        return await Http.SendAsync(request, token);
    }

    private async Task ExchangeAsync(Dictionary<string, string> form, CancellationToken token)
    {
        var response = await PostFormAsync("/connect/token", form, token);
        var body = await response.Content.ReadAsStringAsync(token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        AccessToken = json.RootElement.GetProperty("access_token").GetString();
        RefreshToken = json.RootElement.GetProperty("refresh_token").GetString();
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
