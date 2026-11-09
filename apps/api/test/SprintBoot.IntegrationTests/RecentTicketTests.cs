using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Tickets;
using SprintBoot.Projects;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>Acceptance criteria of Recents: what a person opened lately, newest first, and only what they may still open.</summary>
[Collection(ApiCollection.Name)]
public class RecentTicketTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<ProjectDto> CreateProjectAsync(AuthClient client)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Recents", key = $"R{Random.Shared.Next(100000, 999999)}", type = 0 }, Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
    }

    private static async Task<TicketDto> CreateTicketAsync(AuthClient client, ProjectDto project, string typeName, string title)
    {
        var types = await client.Http.GetFromJsonAsync<List<TicketTypeDto>>($"/api/v1/ticket/types/{project.Id}", Token);
        var typeId = types!.Single(type => type.Name == typeName).Id;
        var response = await client.Http.PostAsJsonAsync("/api/v1/ticket", new { projectId = project.Id, typeId, title }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<TicketDto>(Token))!;
    }

    private static Task<HttpResponseMessage> VisitAsync(AuthClient client, TicketDto ticket) =>
        client.Http.PostAsync($"/api/v1/recent-ticket/visit/{ticket.Id}", null, Token);

    private static async Task<List<string>> RecentKeysAsync(AuthClient client) =>
        (await client.Http.GetFromJsonAsync<List<TicketSummaryDto>>("/api/v1/recent-ticket", Token))!.Select(item => item.Key).ToList();

    [Fact]
    public async Task Recents_list_the_latest_tickets_and_epics_once_each_newest_first()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var first = await CreateTicketAsync(owner, project, "Task", "First");
        var second = await CreateTicketAsync(owner, project, "Story", "Second");
        var epic = await CreateTicketAsync(owner, project, "Epic", "An epic");

        (await RecentKeysAsync(owner)).ShouldBeEmpty();
        foreach (var ticket in new[] { first, second, epic })
        {
            (await VisitAsync(owner, ticket)).EnsureSuccessStatusCode();
        }

        (await RecentKeysAsync(owner)).ShouldBe([epic.Key, second.Key, first.Key]);

        // Opening one again moves it to the top and never adds a second row.
        (await VisitAsync(owner, first)).EnsureSuccessStatusCode();
        (await RecentKeysAsync(owner)).ShouldBe([first.Key, epic.Key, second.Key]);

        // Everyone has their own list.
        var other = await fixture.CreateUserClientAsync(Token);
        (await RecentKeysAsync(other)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Recents_leave_out_what_the_person_can_no_longer_open()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var ticket = await CreateTicketAsync(owner, project, "Task", "Seen by a member");
        var member = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{project.Id}/member", new { userId = member.UserId, role = ProjectRoleNames.Member }, Token)).EnsureSuccessStatusCode();

        (await VisitAsync(member, ticket)).EnsureSuccessStatusCode();
        (await RecentKeysAsync(member)).ShouldBe([ticket.Key]);

        (await owner.Http.DeleteAsync($"/api/v1/project/{project.Id}/member/{member.UserId}", Token)).EnsureSuccessStatusCode();
        (await RecentKeysAsync(member)).ShouldBeEmpty();

        // A stranger cannot note a visit to a ticket they cannot see.
        var stranger = await fixture.CreateUserClientAsync(Token);
        (await VisitAsync(stranger, ticket)).IsSuccessStatusCode.ShouldBeFalse();
        (await RecentKeysAsync(stranger)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Recents_keep_only_the_latest_twenty()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var created = new List<TicketDto>();
        for (var index = 0; index < 22; index++)
        {
            created.Add(await CreateTicketAsync(owner, project, "Task", $"Ticket {index}"));
        }

        foreach (var ticket in created)
        {
            (await VisitAsync(owner, ticket)).EnsureSuccessStatusCode();
        }

        var keys = await RecentKeysAsync(owner);
        keys.Count.ShouldBe(20);
        keys.First().ShouldBe(created.Last().Key);
        keys.ShouldNotContain(created.First().Key);
    }
}
