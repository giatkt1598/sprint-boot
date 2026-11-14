using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace SprintBoot.DocsIntegration;

/// <summary>Docmost said no to the key; the connection needs a new one.</summary>
public class DocmostAuthException(string message) : Exception(message);

/// <summary>Docmost could not be reached or answered with an error.</summary>
public class DocmostUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public record DocmostAccess(string BaseUrl, string ApiKey);

/// <summary>A Docmost page; <see cref="UpdatedAt"/> is when its content last changed, when Docmost says.</summary>
public record DocmostPage(string Id, string SlugId, string Title, string? Icon, bool HasChildren, string SpaceId, DateTime? UpdatedAt = null);

/// <summary>
/// Docmost's own API, as its service user. Every call is a POST under /api and answers {"data": ...}.
/// </summary>
public partial class DocmostClient(IHttpClientFactory httpClients) : ITransientDependency
{
    public const string ClientName = "docmost";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The name of the user the key acts as.</summary>
    public async Task<string> VerifyAsync(DocmostAccess access)
    {
        var data = await PostAsync(access, "users/me", new { });
        var user = data.GetProperty("user");
        return Text(user, "name") ?? Text(user, "email") ?? "?";
    }

    public async Task<List<DocmostSpaceDto>> GetSpacesAsync(DocmostAccess access)
    {
        var spaces = new List<DocmostSpaceDto>();
        string? cursor = null;
        do
        {
            var data = await PostAsync(access, "spaces", new { limit = 100, cursor });
            spaces.AddRange(data.GetProperty("items").EnumerateArray().Select(item => new DocmostSpaceDto
            {
                Id = Text(item, "id")!,
                Name = Text(item, "name") ?? Text(item, "slug")!,
                Slug = Text(item, "slug")!,
            }));
            cursor = NextCursor(data);
        }
        while (cursor is not null);

        return [.. spaces.OrderBy(space => space.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>One level of a space's tree, in Docmost's order.</summary>
    public async Task<List<DocmostPage>> GetPagesAsync(DocmostAccess access, string spaceId, string? parentPageId)
    {
        var pages = new List<DocmostPage>();
        string? cursor = null;
        do
        {
            var data = await PostAsync(access, "pages/sidebar-pages", parentPageId is null
                ? new { spaceId, limit = 100, cursor }
                : (object)new { pageId = parentPageId, limit = 100, cursor });
            pages.AddRange(data.GetProperty("items").EnumerateArray().Select(ToPage));
            cursor = NextCursor(data);
        }
        while (cursor is not null && pages.Count < 500);

        return pages;
    }

    /// <summary>Null when the page does not exist or the service user cannot see it.</summary>
    public async Task<(DocmostPage Page, DocmostSpaceDto Space)?> FindPageAsync(DocmostAccess access, string pageIdOrSlugId)
    {
        JsonElement data;
        try
        {
            data = await PostAsync(access, "pages/info", new { pageId = pageIdOrSlugId });
        }
        catch (DocmostNotFoundException)
        {
            return null;
        }

        var space = data.TryGetProperty("space", out var element) && element.ValueKind == JsonValueKind.Object
            ? new DocmostSpaceDto { Id = Text(element, "id")!, Name = Text(element, "name") ?? string.Empty, Slug = Text(element, "slug")! }
            : null;
        return space is null ? null : (ToPage(data), space);
    }

    public async Task<DocmostPage> CreatePageAsync(DocmostAccess access, string spaceId, string? parentPageId, string title, object content) =>
        ToPage(await PostAsync(access, "pages/create", parentPageId is null
            ? new { spaceId, title, content, format = "json" }
            : (object)new { spaceId, parentPageId, title, content, format = "json" }));

    /// <summary>
    /// The newest pages of a space. Docmost lists a space's pages by when they last changed; of the most recently
    /// changed ones, those created last come first.
    /// </summary>
    public async Task<List<DocmostPage>> GetNewestPagesAsync(DocmostAccess access, string spaceId, int count)
    {
        var data = await PostAsync(access, "pages/recent", new { spaceId, limit = 50 });
        if (!data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. items.EnumerateArray()
            .Select(item => (Page: ToPage(item), Created: Text(item, "createdAt")))
            .OrderByDescending(item => DateTime.TryParse(item.Created, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : DateTime.MinValue)
            .Take(count)
            .Select(item => item.Page)];
    }

    /// <summary>Pages whose title matches, in the spaces the service user reads; one space when given.</summary>
    public async Task<List<(DocmostPage Page, DocmostSpaceDto? Space)>> SearchPagesAsync(DocmostAccess access, string query, string? spaceId)
    {
        var data = await PostAsync(access, "search", spaceId is null
            ? new { query, titleOnly = true, limit = 10 }
            : (object)new { query, spaceId, titleOnly = true, limit = 10 });
        // Keyword search answers {items: [...]}, hybrid search a bare list.
        var items = data.ValueKind == JsonValueKind.Array ? data : data.TryGetProperty("items", out var list) ? list : default;
        if (items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. items.EnumerateArray().Select(item =>
        {
            var space = item.TryGetProperty("space", out var element) && element.ValueKind == JsonValueKind.Object
                ? new DocmostSpaceDto { Id = Text(element, "id") ?? string.Empty, Name = Text(element, "name") ?? string.Empty, Slug = Text(element, "slug") ?? string.Empty }
                : null;
            return (ToPage(item), space);
        })];
    }

    /// <summary>Asks Docmost to uninstall its Sprint Boot integration, with the key it gave for the link.</summary>
    public Task NotifyUnlinkedAsync(DocmostAccess access) => PostAsync(access, "integrations/sprint-boot/unlinked", new { });

    /// <summary>The Docmost spaces offered on Sprint Boot's authorize page, for a link Docmost started.</summary>
    public async Task<List<DocmostSpaceDto>> GetLinkSpacesAsync(string docmostUrl, string state)
    {
        var data = await PostAsync(new DocmostAccess(docmostUrl, string.Empty), "integrations/sprint-boot/link/request", new { state });
        return data.TryGetProperty("spaces", out var spaces) && spaces.ValueKind == JsonValueKind.Array
            ? [.. spaces.EnumerateArray().Select(item => new DocmostSpaceDto
            {
                Id = Text(item, "id")!,
                Name = Text(item, "name") ?? Text(item, "slug")!,
                Slug = Text(item, "slug")!,
            })]
            : [];
    }

    /// <summary>
    /// Finishes a link Docmost started: Docmost installs the Sprint Boot integration with the secret and answers with an
    /// API key of the person who started it.
    /// </summary>
    public async Task<string> CompleteLinkAsync(string docmostUrl, string state, string sprintBootUrl, string bridgeSecret)
    {
        var data = await PostAsync(new DocmostAccess(docmostUrl, string.Empty), "integrations/sprint-boot/link/complete", new { state, sprintBootUrl, bridgeSecret });
        return Text(data, "apiKey") ?? throw new DocmostUnavailableException("Docmost did not hand over an API key.");
    }

    /// <summary>Where a page opens: /s/{space}/p/{title-slug}-{slugId}, as Docmost builds it.</summary>
    public static string PageUrl(string baseUrl, string spaceSlug, string slugId, string title) =>
        $"{baseUrl.TrimEnd('/')}/s/{Uri.EscapeDataString(spaceSlug)}/p/{TitleSlug(title)}-{slugId}";

    public static string SpaceUrl(string baseUrl, string spaceSlug) => $"{baseUrl.TrimEnd('/')}/s/{Uri.EscapeDataString(spaceSlug)}";

    /// <summary>The slug id of a Docmost page address: the part after the last dash of /p/{slug}.</summary>
    public static string? SlugIdFromUrl(string url, string baseUrl)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var root)
            || !string.Equals(uri.Authority, root.Authority, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = PagePath().Match(uri.AbsolutePath);
        return match.Success ? match.Groups["slug"].Value.Split('-')[^1] : null;
    }

    private static string TitleSlug(string title)
    {
        var plain = new StringBuilder();
        foreach (var character in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(character switch { 'đ' => 'd', 'Đ' => 'D', _ => character });
            }
        }

        var slug = NonWord().Replace(plain.ToString().ToLowerInvariant(), "-").Trim('-');
        slug = slug.Length > 70 ? slug[..70].TrimEnd('-') : slug;
        return slug.Length == 0 ? "untitled" : slug;
    }

    private static DocmostPage ToPage(JsonElement item) => new(
        Text(item, "id")!,
        Text(item, "slugId")!,
        Text(item, "title") is { Length: > 0 } title ? title : "Untitled",
        Text(item, "icon"),
        item.TryGetProperty("hasChildren", out var children) && children.ValueKind == JsonValueKind.True,
        Text(item, "spaceId") ?? string.Empty,
        Text(item, "updatedAt") is { } updated && DateTime.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var at)
            ? at
            : null);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? NextCursor(JsonElement data) =>
        data.TryGetProperty("meta", out var meta) && meta.TryGetProperty("hasNextPage", out var more) && more.ValueKind == JsonValueKind.True
            ? Text(meta, "nextCursor")
            : null;

    private async Task<JsonElement> PostAsync(DocmostAccess access, string path, object body)
    {
        var client = httpClients.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{access.BaseUrl.TrimEnd('/')}/api/{path}")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        if (access.ApiKey.Length > 0)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.ApiKey);
        }
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            throw new DocmostUnavailableException($"Could not reach {new Uri(access.BaseUrl).Host}.", failure);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    throw new DocmostAuthException(await ReasonAsync(response, "Docmost refused the API key."));
                case HttpStatusCode.NotFound or HttpStatusCode.Forbidden:
                    throw new DocmostNotFoundException();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DocmostUnavailableException(await ReasonAsync(response, $"Docmost answered {(int)response.StatusCode}."));
            }

            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return document.RootElement.TryGetProperty("data", out var data) ? data.Clone() : document.RootElement.Clone();
            }
            catch (JsonException failure)
            {
                // An address that is not Docmost, such as a login page in front of it.
                throw new DocmostUnavailableException("Docmost did not answer with JSON.", failure);
            }
        }
    }

    /// <summary>Docmost's own message, such as "API access is restricted to workspace admins".</summary>
    private static async Task<string> ReasonAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                && message.GetString() is { Length: > 0 } text
                ? $"{fallback} Docmost said: {text}"
                : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    [GeneratedRegex(@"/p/(?<slug>[^/?#]+)/?$")]
    private static partial Regex PagePath();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonWord();
}

public class DocmostNotFoundException() : Exception("Not found in Docmost.");

/// <summary>Turns Docmost failures into the business errors the UI explains.</summary>
public static class DocmostFailures
{
    public static BusinessException ToBusiness(Exception failure) => failure switch
    {
        DocmostAuthException => new BusinessException(DocsErrors.Unauthorized),
        _ => new BusinessException(DocsErrors.Unavailable),
    };
}
