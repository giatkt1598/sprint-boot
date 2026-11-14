using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Tickets;
using SprintBoot.Projects;
using Xunit;

namespace SprintBoot.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TicketLinkTests(ApiFixture fixture)
{
    private static int _projectCounter;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string NewKey() => $"K{Interlocked.Increment(ref _projectCounter)}{Random.Shared.Next(1000, 9999)}";

    private static async Task<ProjectDto> CreateProjectAsync(AuthClient client)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Links", key = NewKey(), type = 0 }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
    }

    private static async Task<TicketDto> CreateTicketAsync(AuthClient client, ProjectDto project, string title)
    {
        var types = await client.Http.GetFromJsonAsync<List<TicketTypeDto>>($"/api/v1/ticket/types/{project.Id}", Token);
        var response = await client.Http.PostAsJsonAsync("/api/v1/ticket", new { projectId = project.Id, typeId = types!.Single(type => type.Name == "Task").Id, title }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<TicketDto>(Token))!;
    }

    private static async Task<TicketLinkTypeDto> BlocksAsync(AuthClient client) =>
        (await client.Http.GetFromJsonAsync<List<TicketLinkTypeDto>>("/api/v1/ticket-link-type", Token))!.Single(type => type.Name == "Blocks");

    private static Task<HttpResponseMessage> LinkAsync(AuthClient client, TicketDto ticket, Guid typeId, string key, TicketLinkDirection direction = TicketLinkDirection.Outward) =>
        client.Http.PostAsJsonAsync($"/api/v1/ticket-link?ticketId={ticket.Id}", new { linkTypeId = typeId, ticketKey = key, direction }, Token);

    private static async Task<List<TicketLinkDto>> LinksAsync(AuthClient client, TicketDto ticket) =>
        (await client.Http.GetFromJsonAsync<List<TicketLinkDto>>($"/api/v1/ticket-link?ticketId={ticket.Id}", Token))!;

    [Fact]
    public async Task The_default_link_types_are_ready_for_every_workspace()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var types = (await client.Http.GetFromJsonAsync<List<TicketLinkTypeDto>>("/api/v1/ticket-link-type", Token))!;
        types.Select(type => type.Outward).ShouldContain("blocks");
        types.Select(type => type.Inward).ShouldContain("is duplicated by");
        types.Select(type => type.Outward).ShouldContain("clones");
    }

    [Fact]
    public async Task A_link_reads_its_own_way_on_both_tickets_and_is_written_to_both_histories()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var first = await CreateTicketAsync(client, project, "Login");
        var second = await CreateTicketAsync(client, project, "Signup");
        var blocks = await BlocksAsync(client);

        var created = await LinkAsync(client, first, blocks.Id, second.Key.ToLowerInvariant());
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Token));
        var link = (await created.Content.ReadFromJsonAsync<TicketLinkDto>(Token))!;
        link.Phrase.ShouldBe("blocks");
        link.Ticket!.Key.ShouldBe(second.Key);

        var seen = (await LinksAsync(client, second)).ShouldHaveSingleItem();
        seen.Phrase.ShouldBe("is blocked by");
        seen.Direction.ShouldBe(TicketLinkDirection.Inward);
        seen.Ticket!.Key.ShouldBe(first.Key);

        (await (await LinkAsync(client, second, blocks.Id, first.Key, TicketLinkDirection.Inward)).Content.ReadAsStringAsync(Token)).ShouldContain(TicketLinkErrors.Duplicate);
        (await (await LinkAsync(client, first, blocks.Id, first.Key)).Content.ReadAsStringAsync(Token)).ShouldContain(TicketLinkErrors.Self);

        var history = (await client.Http.GetFromJsonAsync<List<TicketActivityDto>>($"/api/v1/ticket/{second.Id}/activity", Token))!;
        history.ShouldContain(item => item.Field == "link" && item.NewValue == $"is blocked by {first.Key}");

        (await client.Http.DeleteAsync($"/api/v1/ticket-link/{link.Id}", Token)).EnsureSuccessStatusCode();
        (await LinksAsync(client, first)).ShouldBeEmpty();
        (await LinksAsync(client, second)).ShouldBeEmpty();
        (await client.Http.GetFromJsonAsync<List<TicketActivityDto>>($"/api/v1/ticket/{first.Id}/activity", Token))!
            .ShouldContain(item => item.Field == "link" && item.OldValue == $"blocks {second.Key}");
    }

    [Fact]
    public async Task Links_cross_spaces_but_only_into_ones_the_person_can_see()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var mine = await CreateProjectAsync(owner);
        var ticket = await CreateTicketAsync(owner, mine, "Mine");
        var blocks = await BlocksAsync(owner);

        var stranger = await fixture.CreateUserClientAsync(Token);
        var theirs = await CreateProjectAsync(stranger);
        var hidden = await CreateTicketAsync(stranger, theirs, "Hidden");

        // A ticket they cannot see looks the same as one that does not exist.
        (await LinkAsync(owner, ticket, blocks.Id, hidden.Key)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var shared = await CreateProjectAsync(owner);
        var other = await CreateTicketAsync(owner, shared, "Other space");
        (await LinkAsync(owner, ticket, blocks.Id, other.Key)).EnsureSuccessStatusCode();

        // A pasted ticket address works as well as its key.
        var third = await CreateTicketAsync(owner, shared, "Pasted");
        (await LinkAsync(owner, other, blocks.Id, $"https://sprint-boot.example.com/browse/{third.Key}?tab=history")).EnsureSuccessStatusCode();
        (await LinksAsync(owner, other)).ShouldContain(link => link.Ticket!.Key == third.Key);
        (await LinkAsync(owner, third, blocks.Id, $"http://localhost:5173/browse/{third.Key}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A member of only the first space sees the link, not what is behind it.
        var member = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{mine.Id}/member", new { userId = member.UserId, role = ProjectRoleNames.Viewer }, Token)).EnsureSuccessStatusCode();
        var restricted = (await LinksAsync(member, ticket)).ShouldHaveSingleItem();
        restricted.Ticket.ShouldBeNull();
        restricted.Phrase.ShouldBe("blocks");
        (await member.Http.DeleteAsync($"/api/v1/ticket-link/{restricted.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await LinkAsync(member, ticket, blocks.Id, other.Key)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_admins_change_link_types_and_a_type_in_use_stays()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        var name = $"Tests {Guid.NewGuid():N}";

        var created = await admin.Http.PostAsJsonAsync("/api/v1/ticket-link-type", new { name, outward = "tests", inward = "is tested by" }, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Token));
        var type = (await created.Content.ReadFromJsonAsync<TicketLinkTypeDto>(Token))!;
        (await (await admin.Http.PostAsJsonAsync("/api/v1/ticket-link-type", new { name = name.ToUpperInvariant(), outward = "a", inward = "b" }, Token)).Content.ReadAsStringAsync(Token))
            .ShouldContain(TicketLinkErrors.TypeNameTaken);

        var user = await fixture.CreateUserClientAsync(Token);
        (await user.Http.PutAsJsonAsync($"/api/v1/ticket-link-type/{type.Id}", new { name, outward = "x", inward = "y" }, Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var project = await CreateProjectAsync(user);
        var first = await CreateTicketAsync(user, project, "One");
        var second = await CreateTicketAsync(user, project, "Two");
        (await LinkAsync(user, first, type.Id, second.Key)).EnsureSuccessStatusCode();

        (await admin.Http.PutAsJsonAsync($"/api/v1/ticket-link-type/{type.Id}", new { name, outward = "verifies", inward = "is verified by" }, Token)).EnsureSuccessStatusCode();
        (await LinksAsync(user, second)).ShouldHaveSingleItem().Phrase.ShouldBe("is verified by");
        (await (await admin.Http.DeleteAsync($"/api/v1/ticket-link-type/{type.Id}", Token)).Content.ReadAsStringAsync(Token)).ShouldContain(TicketLinkErrors.TypeInUse);
    }
}
