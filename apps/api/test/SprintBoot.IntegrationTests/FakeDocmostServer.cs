using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SprintBoot.IntegrationTests;

/// <summary>Speaks just enough of Docmost's API for the integration; test code decides when it is down.</summary>
public sealed class FakeDocmostServer : HttpMessageHandler
{
    public const string BaseUrl = "http://docmost.fake";
    public const string ValidKey = "docmost-key";
    public const string SpaceId = "11111111-1111-1111-1111-111111111111";
    public const string SpaceSlug = "engineering";
    public const string PageId = "22222222-2222-2222-2222-222222222222";
    public const string PageSlugId = "abc123";

    public static readonly FakeDocmostServer Instance = new();

    public bool Down { get; set; }

    /// <summary>Bodies of the pages/create calls, newest last.</summary>
    public ConcurrentQueue<JsonNode> CreatedPages { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Down)
        {
            throw new HttpRequestException("Connection refused");
        }

        if (request.Headers.Authorization?.Parameter != ValidKey)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"message":"Invalid API key","error":"Unauthorized","statusCode":401}""", Encoding.UTF8, "application/json"),
            };
        }

        var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
        var space = new { id = SpaceId, name = "Engineering", slug = SpaceSlug };
        var page = new { id = PageId, slugId = PageSlugId, title = "Design notes", icon = (string?)null, spaceId = SpaceId, hasChildren = true, space };
        var meta = new { hasNextPage = false, nextCursor = (string?)null };

        return request.RequestUri!.AbsolutePath switch
        {
            "/api/users/me" => Data(new { user = new { name = "Docs Bot", email = "bot@docmost.fake" } }),
            "/api/spaces" => Data(new { items = new[] { space }, meta }),
            "/api/pages/sidebar-pages" => Data(new
            {
                items = body?["pageId"] is null
                    ? new object[] { page }
                    : [new { id = "33333333-3333-3333-3333-333333333333", slugId = "child1", title = "Child", icon = (string?)null, spaceId = SpaceId, hasChildren = false }],
                meta,
            }),
            "/api/pages/info" when body?["pageId"]?.GetValue<string>() is PageId or PageSlugId => Data(page),
            "/api/pages/info" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/api/pages/create" => Created(body!),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private HttpResponseMessage Created(JsonNode body)
    {
        CreatedPages.Enqueue(body);
        return Data(new { id = Guid.NewGuid().ToString(), slugId = $"new{CreatedPages.Count}", title = body["title"]!.GetValue<string>(), spaceId = SpaceId });
    }

    private static HttpResponseMessage Data(object data) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { data, success = true, status = 200 }), Encoding.UTF8, "application/json"),
    };
}
