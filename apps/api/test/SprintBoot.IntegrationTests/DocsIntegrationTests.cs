using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SprintBoot.DocsIntegration;
using SprintBoot.Projects;
using SprintBoot.Tickets;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>
/// SB-28: the two-way link with Docmost. The connection is one per workspace, so every test here sets it up again and
/// they all run in this one class, one after another.
/// </summary>
[Collection(ApiCollection.Name)]
public class DocsIntegrationTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Space(AuthClient Owner, ProjectDto Project, TicketDto Ticket);

    private async Task<AuthClient> AdminAsync()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        return admin;
    }

    /// <summary>Connects to the fake Docmost and returns the bridge secret Docmost would be given.</summary>
    private async Task<string> ConnectAsync(AuthClient admin)
    {
        FakeDocmostServer.Instance.Down = false;
        var saved = await admin.Http.PostAsJsonAsync("/api/v1/docs-admin/save", new { baseUrl = FakeDocmostServer.BaseUrl, apiKey = FakeDocmostServer.ValidKey }, Token);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        var secret = await admin.Http.PostAsync("/api/v1/docs-admin/rotate-secret", null, Token);
        return (await secret.Content.ReadFromJsonAsync<DocsBridgeSecretDto>(Token))!.Secret;
    }

    private async Task<Space> SpaceAsync()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var key = $"D{Random.Shared.Next(100000, 999999)}";
        var project = (await (await owner.Http.PostAsJsonAsync("/api/v1/project", new { name = "Docs", key, type = 0 }, Token)).Content.ReadFromJsonAsync<ProjectDto>(Token))!;
        var types = await owner.Http.GetFromJsonAsync<List<TicketTypeDto>>($"/api/v1/ticket/types/{project.Id}", Token);
        var created = await owner.Http.PostAsJsonAsync("/api/v1/ticket", new { projectId = project.Id, typeId = types!.Single(type => type.Name == "Task").Id, title = "Login page" }, Token);
        return new Space(owner, project, (await created.Content.ReadFromJsonAsync<TicketDto>(Token))!);
    }

    private HttpClient Docmost(string secret)
    {
        var http = fixture.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return http;
    }

    private static Task<TicketDocsDto?> PagesAsync(AuthClient client, TicketDto ticket) =>
        client.Http.GetFromJsonAsync<TicketDocsDto>($"/api/v1/docs-ticket?ticketId={ticket.Id}", Token);

    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        return JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Saving_the_connection_checks_it_and_explains_a_wrong_key_or_a_dead_server()
    {
        var admin = await AdminAsync();

        var good = await admin.Http.PostAsJsonAsync("/api/v1/docs-admin/save", new { baseUrl = FakeDocmostServer.BaseUrl + "/", apiKey = FakeDocmostServer.ValidKey }, Token);
        var connected = (await good.Content.ReadFromJsonAsync<DocsConnectionDto>(Token))!;
        connected.Status.ShouldBe(DocsConnectionStatus.Connected);
        connected.BaseUrl.ShouldBe(FakeDocmostServer.BaseUrl);
        connected.DocmostUser.ShouldBe("Docs Bot");
        connected.SprintBootUrl.ShouldNotEndWith("/docs-bridge");

        var wrong = await admin.Http.PostAsJsonAsync("/api/v1/docs-admin/save", new { baseUrl = FakeDocmostServer.BaseUrl, apiKey = "wrong" }, Token);
        var refused = (await wrong.Content.ReadFromJsonAsync<DocsConnectionDto>(Token))!;
        refused.Problem.ShouldBe(DocsProblem.Unauthorized);
        refused.ProblemDetail!.ShouldContain("Invalid API key");

        await ConnectAsync(admin);
        FakeDocmostServer.Instance.Down = true;
        var down = await admin.Http.PostAsync("/api/v1/docs-admin/test", null, Token);
        (await down.Content.ReadFromJsonAsync<DocsConnectionDto>(Token))!.Problem.ShouldBe(DocsProblem.Unavailable);

        var (owner, project, _) = await SpaceAsync();
        var spaces = await owner.Http.GetAsync($"/api/v1/docs-project/docmost-spaces/{project.Id}", Token);
        spaces.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(spaces)).ShouldBe(DocsErrors.Unavailable);
        FakeDocmostServer.Instance.Down = false;

        // Only those with the permission key see or change the connection.
        (await owner.Http.GetAsync("/api/v1/docs-admin", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_space_links_a_Docmost_space_and_shows_its_page_tree()
    {
        await ConnectAsync(await AdminAsync());
        var (owner, project, _) = await SpaceAsync();

        var choices = await owner.Http.GetFromJsonAsync<List<DocmostSpaceDto>>($"/api/v1/docs-project/docmost-spaces/{project.Id}", Token);
        choices!.ShouldHaveSingleItem().Slug.ShouldBe(FakeDocmostServer.SpaceSlug);

        var linked = await owner.Http.PostAsJsonAsync($"/api/v1/docs-project/link/{project.Id}", new { docmostSpaceId = FakeDocmostServer.SpaceId }, Token);
        var docs = (await linked.Content.ReadFromJsonAsync<ProjectDocsDto>(Token))!;
        docs.Space!.Name.ShouldBe("Engineering");
        docs.SpaceUrl.ShouldBe($"{FakeDocmostServer.BaseUrl}/s/{FakeDocmostServer.SpaceSlug}");

        var top = (await owner.Http.GetFromJsonAsync<List<DocsPageNodeDto>>($"/api/v1/docs-project/pages?projectId={project.Id}", Token))!.ShouldHaveSingleItem();
        top.HasChildren.ShouldBeTrue();
        top.Url.ShouldBe($"{FakeDocmostServer.BaseUrl}/s/{FakeDocmostServer.SpaceSlug}/p/design-notes-{FakeDocmostServer.PageSlugId}");
        var children = await owner.Http.GetFromJsonAsync<List<DocsPageNodeDto>>($"/api/v1/docs-project/pages?projectId={project.Id}&parentPageId={top.Id}", Token);
        children!.ShouldHaveSingleItem().Title.ShouldBe("Child");

        // Readers see the tree; only managers pick the space.
        var outsider = await fixture.CreateUserClientAsync(Token);
        (await outsider.Http.GetAsync($"/api/v1/docs-project?projectId={project.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pages_link_to_a_ticket_by_hand_and_a_new_page_carries_a_card_back_to_it()
    {
        await ConnectAsync(await AdminAsync());
        var (owner, project, ticket) = await SpaceAsync();

        var invalid = await owner.Http.PostAsJsonAsync($"/api/v1/docs-ticket/link/{ticket.Id}", new { url = "http://elsewhere.fake/p/x-abc123" }, Token);
        (await CodeAsync(invalid)).ShouldBe(DocsErrors.InvalidPageUrl);
        var missing = await owner.Http.PostAsJsonAsync($"/api/v1/docs-ticket/link/{ticket.Id}", new { url = $"{FakeDocmostServer.BaseUrl}/s/x/p/gone-zzz999" }, Token);
        (await CodeAsync(missing)).ShouldBe(DocsErrors.PageNotFound);

        var link = await owner.Http.PostAsJsonAsync($"/api/v1/docs-ticket/link/{ticket.Id}", new { url = $"{FakeDocmostServer.BaseUrl}/s/{FakeDocmostServer.SpaceSlug}/p/design-notes-{FakeDocmostServer.PageSlugId}" }, Token);
        link.StatusCode.ShouldBe(HttpStatusCode.OK, await link.Content.ReadAsStringAsync(Token));
        var page = (await PagesAsync(owner, ticket))!.Linked.ShouldHaveSingleItem();
        page.Title.ShouldBe("Design notes");
        page.Source.ShouldBe(DocLinkSource.Manual);

        // Creating a page needs the space linked first.
        var unlinked = await owner.Http.PostAsJsonAsync($"/api/v1/docs-ticket/page/{ticket.Id}", new { }, Token);
        (await CodeAsync(unlinked)).ShouldBe(DocsErrors.SpaceNotLinked);
        (await owner.Http.PostAsJsonAsync($"/api/v1/docs-project/link/{project.Id}", new { docmostSpaceId = FakeDocmostServer.SpaceId }, Token)).EnsureSuccessStatusCode();

        var created = await owner.Http.PostAsJsonAsync($"/api/v1/docs-ticket/page/{ticket.Id}", new { }, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Token));
        var sent = FakeDocmostServer.Instance.CreatedPages.Last();
        sent["title"]!.GetValue<string>().ShouldBe($"{ticket.Key} Login page");
        sent["spaceId"]!.GetValue<string>().ShouldBe(FakeDocmostServer.SpaceId);
        sent["parentPageId"].ShouldBeNull();
        var card = sent["content"]!["content"]![0]!;
        card["type"]!.GetValue<string>().ShouldBe("integrationCard");
        card["attrs"]!["url"]!.GetValue<string>().ShouldEndWith($"/browse/{ticket.Key}");
        card["attrs"]!["provider"]!.GetValue<string>().ShouldBe("sprint-boot");
        (await PagesAsync(owner, ticket))!.Linked.Count.ShouldBe(2);

        // A child page, with its own title, under a page picked from the space's search.
        var parents = await owner.Http.GetFromJsonAsync<List<DocsPageHitDto>>($"/api/v1/docs-project/search-pages/{project.Id}?query=Design", Token);
        var parent = parents!.ShouldHaveSingleItem();
        (await owner.Http.PostAsJsonAsync($"/api/v1/docs-ticket/page/{ticket.Id}", new { title = "Login design", parentPageId = parent.Id }, Token)).EnsureSuccessStatusCode();
        var child = FakeDocmostServer.Instance.CreatedPages.Last();
        child["title"]!.GetValue<string>().ShouldBe("Login design");
        child["parentPageId"]!.GetValue<string>().ShouldBe(FakeDocmostServer.PageId);

        (await owner.Http.DeleteAsync($"/api/v1/docs-ticket/{page.Id}", Token)).EnsureSuccessStatusCode();
        (await PagesAsync(owner, ticket))!.Linked.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_page_found_by_search_links_by_id()
    {
        await ConnectAsync(await AdminAsync());
        var (owner, _, ticket) = await SpaceAsync();

        var hits = await owner.Http.GetFromJsonAsync<List<DocsPageHitDto>>($"/api/v1/docs-ticket/search-pages/{ticket.Id}?query=design", Token);
        var hit = hits!.ShouldHaveSingleItem();
        hit.SpaceName.ShouldBe("Engineering");
        hit.Url.ShouldEndWith($"/s/{FakeDocmostServer.SpaceSlug}/p/design-notes-{FakeDocmostServer.PageSlugId}");

        (await owner.Http.PostAsJsonAsync($"/api/v1/docs-ticket/link/{ticket.Id}", new { pageId = hit.Id }, Token)).EnsureSuccessStatusCode();
        (await PagesAsync(owner, ticket))!.Linked.ShouldHaveSingleItem().Title.ShouldBe("Design notes");
    }

    [Fact]
    public async Task Adding_a_document_offers_the_newest_pages_of_the_linked_space()
    {
        await ConnectAsync(await AdminAsync());
        var (owner, project, ticket) = await SpaceAsync();
        (await owner.Http.GetFromJsonAsync<List<DocsPageHitDto>>($"/api/v1/docs-ticket/newest-pages/{ticket.Id}", Token))!.ShouldBeEmpty();

        (await owner.Http.PostAsJsonAsync($"/api/v1/docs-project/link/{project.Id}", new { docmostSpaceId = FakeDocmostServer.SpaceId }, Token)).EnsureSuccessStatusCode();
        var newest = await owner.Http.GetFromJsonAsync<List<DocsPageHitDto>>($"/api/v1/docs-ticket/newest-pages/{ticket.Id}", Token);

        newest!.Select(page => page.Title).ShouldBe(["Brand new", "Old but edited"]);
        newest[0].SpaceName.ShouldBe("Engineering");
        newest[0].Url.ShouldEndWith($"/s/{FakeDocmostServer.SpaceSlug}/p/brand-new-new222");
    }

    [Fact]
    public async Task A_Docmost_address_resolves_for_a_chip()
    {
        await ConnectAsync(await AdminAsync());
        var reader = await fixture.CreateUserClientAsync(Token);

        var info = await reader.Http.GetFromJsonAsync<DocsInfoDto>("/api/v1/docs-page/info", Token);
        info!.DocmostUrl.ShouldBe(FakeDocmostServer.BaseUrl);
        var address = Uri.EscapeDataString($"{FakeDocmostServer.BaseUrl}/s/{FakeDocmostServer.SpaceSlug}/p/design-notes-{FakeDocmostServer.PageSlugId}");
        var resolved = await reader.Http.GetFromJsonAsync<DocsPageRefDto>($"/api/v1/docs-page/resolve?url={address}", Token);
        resolved!.PageId.ShouldBe(FakeDocmostServer.PageId);
        resolved.Title.ShouldBe("Design notes");
        (await reader.Http.GetAsync($"/api/v1/docs-page/resolve?url={Uri.EscapeDataString("http://elsewhere.fake/p/abc123")}", Token))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Chips_in_the_description_and_comments_follow_the_text()
    {
        await ConnectAsync(await AdminAsync());
        var (owner, _, ticket) = await SpaceAsync();
        static string Chip(string pageId, string title) =>
            $"<p>See <a data-type=\"doc-mention\" class=\"doc-mention\" data-page-id=\"{pageId}\" href=\"{FakeDocmostServer.BaseUrl}/p/{pageId}\">{title}</a></p>";

        (await owner.Http.PutAsJsonAsync($"/api/v1/ticket/{ticket.Id}", new { concurrencyStamp = ticket.ConcurrencyStamp, fields = new { description = Chip("page-a", "Spec A") } }, Token))
            .EnsureSuccessStatusCode();
        var comment = await (await owner.Http.PostAsJsonAsync("/api/v1/comment", new { ticketId = ticket.Id, body = Chip("page-b", "Spec B") }, Token))
            .Content.ReadFromJsonAsync<CommentDto>(Token);

        var linked = await WaitForAsync(owner, ticket, docs => docs.Linked.Count == 2);
        linked.Linked.ShouldAllBe(item => item.Source == DocLinkSource.Text);
        linked.Linked.Select(item => item.Title).ShouldBe(["Spec A", "Spec B"], ignoreOrder: true);

        // Taking the chip out of the comment takes its page out; the description's stays.
        (await owner.Http.PutAsJsonAsync($"/api/v1/comment/{comment!.Id}", new { body = "<p>never mind</p>" }, Token)).EnsureSuccessStatusCode();
        (await WaitForAsync(owner, ticket, docs => docs.Linked.Count == 1)).Linked.Single().Title.ShouldBe("Spec A");

        // A chip cannot be unlinked by hand: it goes with its text.
        var chipLink = (await PagesAsync(owner, ticket))!.Linked.Single();
        (await CodeAsync(await owner.Http.DeleteAsync($"/api/v1/docs-ticket/{chipLink.Id}", Token))).ShouldBe(DocsErrors.MentionLink);
    }

    private static async Task<TicketDocsDto> WaitForAsync(AuthClient client, TicketDto ticket, Func<TicketDocsDto, bool> done)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var docs = (await PagesAsync(client, ticket))!;
            if (done(docs))
            {
                return docs;
            }

            await Task.Delay(200, Token);
        }

        throw new TimeoutException("The ticket's text links did not settle.");
    }

    [Fact]
    public async Task Docmost_reports_the_tickets_a_page_mentions_and_the_links_follow_every_save()
    {
        var secret = await ConnectAsync(await AdminAsync());
        var (owner, project, ticket) = await SpaceAsync();
        var other = (await (await owner.Http.PostAsJsonAsync("/api/v1/ticket", new { projectId = project.Id, typeId = ticket.Type.Id, title = "Logout" }, Token))
            .Content.ReadFromJsonAsync<TicketDto>(Token))!;
        var docmost = Docmost(secret);
        var pageId = Guid.NewGuid().ToString();
        Task<HttpResponseMessage> SaveAsync(object body) => docmost.PostAsJsonAsync("/api/v1/docs-bridge/page-links", body, Token);

        (await SaveAsync(new { pageId, title = "Spec", url = "http://docmost.fake/s/e/p/spec-1", keys = new[] { ticket.Key }, urls = new[] { $"http://sb/browse/{other.Key}" } }))
            .EnsureSuccessStatusCode();
        (await PagesAsync(owner, ticket))!.Mentions.ShouldHaveSingleItem().Source.ShouldBe(DocLinkSource.Mention);
        (await PagesAsync(owner, ticket))!.Linked.ShouldBeEmpty();
        (await PagesAsync(owner, other))!.Mentions.ShouldHaveSingleItem();

        // The same save again changes nothing; dropping a mention drops its link, and the title follows the page.
        (await SaveAsync(new { pageId, title = "Spec", url = "http://docmost.fake/s/e/p/spec-1", keys = new[] { ticket.Key, other.Key } })).EnsureSuccessStatusCode();
        (await SaveAsync(new { pageId, title = "Spec v2", url = "http://docmost.fake/s/e/p/spec-v2-1", keys = new[] { ticket.Key } })).EnsureSuccessStatusCode();
        (await PagesAsync(owner, ticket))!.Mentions.ShouldHaveSingleItem().Title.ShouldBe("Spec v2");
        (await PagesAsync(owner, other))!.Mentions.ShouldBeEmpty();

        (await SaveAsync(new { pageId, deleted = true })).EnsureSuccessStatusCode();
        (await PagesAsync(owner, ticket))!.Mentions.ShouldBeEmpty();

        (await Docmost("wrong-secret").PostAsJsonAsync("/api/v1/docs-bridge/page-links", new { pageId, keys = new[] { ticket.Key } }, Token))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Docmost_unfurls_tickets_and_creates_them_as_the_person_by_email()
    {
        var secret = await ConnectAsync(await AdminAsync());
        var (owner, project, ticket) = await SpaceAsync();
        var docmost = Docmost(secret);

        var unfurl = await docmost.PostAsJsonAsync("/api/v1/docs-bridge/unfurl", new { urls = new[] { $"http://sb/browse/{ticket.Key}" }, keys = new[] { "NOPE-1" } }, Token);
        var view = JsonDocument.Parse(await unfurl.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("tickets").EnumerateArray().Single();
        view.GetProperty("title").GetString().ShouldBe("Login page");
        view.GetProperty("statusCategory").GetString().ShouldBe("todo");
        view.GetProperty("url").GetString().ShouldEndWith($"/browse/{ticket.Key}");

        var me = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", Token);
        var email = me.GetProperty("email").GetString()!;
        var created = await docmost.PostAsJsonAsync("/api/v1/docs-bridge/tickets", new { actorEmail = email, spaceKey = project.Key, title = "From a page", description = "line one\nline two" }, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Token));
        var key = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("key").GetString()!;
        var stored = (await owner.Http.GetFromJsonAsync<TicketDto>($"/api/v1/ticket/by-key?key={key}", Token))!;
        stored.Reporter!.Email.ShouldBe(email);
        stored.Description.ShouldBe("<p>line one</p><p>line two</p>");

        var stranger = await docmost.PostAsJsonAsync("/api/v1/docs-bridge/tickets", new { actorEmail = "nobody@test.local", spaceKey = project.Key, title = "x" }, Token);
        (await CodeAsync(stranger)).ShouldBe(DocsErrors.UnknownUser);

        // Someone who cannot see the space cannot create in it either.
        var outsider = await fixture.CreateUserClientAsync(Token);
        var outsiderEmail = (await outsider.Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/me", Token)).GetProperty("email").GetString();
        (await docmost.PostAsJsonAsync("/api/v1/docs-bridge/tickets", new { actorEmail = outsiderEmail, spaceKey = project.Key, title = "x" }, Token))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var found = await docmost.PostAsJsonAsync("/api/v1/docs-bridge/search", new { actorEmail = email, text = "From a page" }, Token);
        JsonDocument.Parse(await found.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("tickets").EnumerateArray()
            .Select(item => item.GetProperty("key").GetString()).ShouldContain(key);
        var outsiderSearch = await docmost.PostAsJsonAsync("/api/v1/docs-bridge/search", new { actorEmail = outsiderEmail, text = "From a page" }, Token);
        JsonDocument.Parse(await outsiderSearch.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("tickets").GetArrayLength().ShouldBe(0);
    }
}
