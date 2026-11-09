using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Agile;
using SprintBoot.Tickets;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>Acceptance criteria of the space Overview: the Summary figures and the Introduction page.</summary>
[Collection(ApiCollection.Name)]
public class OverviewTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<ProjectDto> CreateProjectAsync(AuthClient client)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Overview", key = $"O{Random.Shared.Next(100000, 999999)}", type = 0 }, Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
    }

    private static async Task<TicketDto> CreateTicketAsync(AuthClient client, ProjectDto project, string typeName, object? assigneeId = null, decimal? points = null, Guid? parentId = null)
    {
        var types = await client.Http.GetFromJsonAsync<List<TicketTypeDto>>($"/api/v1/ticket/types/{project.Id}", Token);
        var typeId = types!.Single(type => type.Name == typeName).Id;
        var response = await client.Http.PostAsJsonAsync("/api/v1/ticket", new { projectId = project.Id, typeId, title = $"{typeName} work", assigneeId, storyPoints = points, parentId }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<TicketDto>(Token))!;
    }

    private static async Task FinishAsync(AuthClient client, TicketDto ticket)
    {
        var reloaded = await client.Http.GetFromJsonAsync<TicketDto>($"/api/v1/ticket/by-key?key={ticket.Key}", Token);
        var available = await client.Http.GetFromJsonAsync<List<AvailableTransitionDto>>($"/api/v1/transition/available/{ticket.Id}", Token);
        var done = available!.Single(item => item.ToStatus.Category == StatusCategory.Done);
        (await client.Http.PostAsJsonAsync(
            $"/api/v1/transition/execute/{ticket.Id}",
            new { transitionId = done.Id, concurrencyStamp = reloaded!.ConcurrencyStamp },
            Token)).EnsureSuccessStatusCode();
    }

    private static async Task<OverviewSummaryDto> SummaryAsync(AuthClient client, ProjectDto project, OverviewPeriod period = OverviewPeriod.CurrentSprint)
    {
        var response = await client.Http.GetAsync($"/api/v1/project-overview/summary/{project.Id}?Period={(int)period}", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<OverviewSummaryDto>(Token))!;
    }

    [Fact]
    public async Task Summary_counts_the_tickets_of_the_sprint_and_velocity_of_closed_sprints()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var finished = await CreateTicketAsync(owner, project, "Task", owner.UserId, 5);
        var open = await CreateTicketAsync(owner, project, "Task", owner.UserId, 3);
        await CreateTicketAsync(owner, project, "Bug");
        await CreateTicketAsync(owner, project, "Epic");
        await CreateTicketAsync(owner, project, "Subtask", parentId: open.Id);

        var sprintResponse = await owner.Http.PostAsJsonAsync("/api/v1/sprint", new { projectId = project.Id, name = "Sprint 1" }, Token);
        var sprint = (await sprintResponse.Content.ReadFromJsonAsync<SprintSummaryDto>(Token))!;
        foreach (var ticket in new[] { finished, open })
        {
            (await owner.Http.PostAsJsonAsync($"/api/v1/sprint/move-ticket/{ticket.Id}", new { sprintId = sprint.Id }, Token)).EnsureSuccessStatusCode();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await owner.Http.PostAsJsonAsync(
            $"/api/v1/sprint/{sprint.Id}/start",
            new { name = sprint.Name, startDate = today.AddDays(-3).ToString("yyyy-MM-dd"), endDate = today.AddDays(4).ToString("yyyy-MM-dd") },
            Token)).EnsureSuccessStatusCode();
        await FinishAsync(owner, finished);

        var running = await SummaryAsync(owner, project);
        running.Range.SprintName.ShouldBe("Sprint 1");
        running.Range.DaysLeft.ShouldBe(5);
        running.Range.BySprint.ShouldBeTrue();
        running.Members.ShouldBe(1);
        running.MembersAddedInPeriod.ShouldBe(1);

        // Only the sprint's tickets count, as the Board shows them: not its subtasks, nor the bug and the epic outside it.
        running.Tickets.Total.ShouldBe(2);
        running.Tickets.CreatedInPeriod.ShouldBe(2);
        running.Tickets.Done.ShouldBe(1);
        running.Tickets.ToDo.ShouldBe(1);
        running.ByType.Select(item => (item.Name, item.Count)).ShouldBe([("Task", 2)]);
        running.Activity.Completed.ShouldBe(1);
        running.Activity.LastCompletedAt.ShouldNotBeNull();
        running.PullRequests.Total.ShouldBe(0);

        // The running sprint counts at its progress so far: the finished task's points, not the open one's.
        running.VelocitySprints.ShouldBe(1);
        var progress = running.Velocity.ShouldHaveSingleItem();
        progress.User.Id.ShouldBe(owner.UserId);
        progress.Points.ShouldBe(5);
        progress.Tickets.ShouldBe(1);
        running.Recent.ShouldContain(item => item.Kind == TicketEventKind.Changed && item.Field == "status" && item.TicketKey == finished.Key);
        running.Recent.Count.ShouldBe(5);

        (await owner.Http.PostAsJsonAsync($"/api/v1/sprint/{sprint.Id}/complete", new { toBacklog = true }, Token)).EnsureSuccessStatusCode();

        var closed = await SummaryAsync(owner, project, OverviewPeriod.ThisYear);
        closed.Range.From.ShouldBe(new DateOnly(today.Year, 1, 1));
        closed.Range.SprintName.ShouldBeNull();
        closed.Range.BySprint.ShouldBeFalse();
        closed.Tickets.Total.ShouldBe(5);
        // A calendar period counts the work done in its dates, not the last sprints.
        closed.VelocitySprints.ShouldBe(0);
        var velocity = closed.Velocity.ShouldHaveSingleItem();
        velocity.User.Id.ShouldBe(owner.UserId);
        velocity.Points.ShouldBe(5);
        velocity.Tickets.ShouldBe(1);
    }

    private static async Task<SprintSummaryDto> SprintAsync(AuthClient client, ProjectDto project, string name, params TicketDto[] tickets)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/sprint", new { projectId = project.Id, name }, Token);
        var sprint = (await response.Content.ReadFromJsonAsync<SprintSummaryDto>(Token))!;
        foreach (var ticket in tickets)
        {
            (await client.Http.PostAsJsonAsync($"/api/v1/sprint/move-ticket/{ticket.Id}", new { sprintId = sprint.Id }, Token)).EnsureSuccessStatusCode();
        }

        return sprint;
    }

    private static async Task StartAsync(AuthClient client, SprintSummaryDto sprint, int startDaysAgo, int endInDays)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await client.Http.PostAsJsonAsync(
            $"/api/v1/sprint/{sprint.Id}/start",
            new { name = sprint.Name, startDate = today.AddDays(-startDaysAgo).ToString("yyyy-MM-dd"), endDate = today.AddDays(endInDays).ToString("yyyy-MM-dd") },
            Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task Sprint_figures_follow_what_is_in_the_sprint_not_its_dates()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var earlier = await CreateTicketAsync(owner, project, "Task", owner.UserId, 5);
        var first = await SprintAsync(owner, project, "Sprint 1", earlier);
        await StartAsync(owner, first, 20, -10);
        await FinishAsync(owner, earlier);
        (await owner.Http.PostAsJsonAsync($"/api/v1/sprint/{first.Id}/complete", new { toBacklog = true }, Token)).EnsureSuccessStatusCode();

        var late = await CreateTicketAsync(owner, project, "Task", owner.UserId, 3);
        var movedOut = await CreateTicketAsync(owner, project, "Bug", owner.UserId, 8);
        var second = await SprintAsync(owner, project, "Sprint 2", late, movedOut);
        // The planned end has passed, but the sprint still runs.
        await StartAsync(owner, second, 9, -2);
        (await owner.Http.PostAsJsonAsync($"/api/v1/sprint/move-ticket/{movedOut.Id}", new { sprintId = (Guid?)null }, Token)).EnsureSuccessStatusCode();
        await FinishAsync(owner, late);

        var current = await SummaryAsync(owner, project);
        current.Range.SprintName.ShouldBe("Sprint 2");
        // The ticket moved back to the backlog no longer counts; the one done after the planned end does.
        current.Tickets.Total.ShouldBe(1);
        current.Tickets.Done.ShouldBe(1);
        current.Activity.Completed.ShouldBe(1);
        // Velocity is the running sprint's alone, not the closed sprint's before it.
        current.VelocitySprints.ShouldBe(1);
        current.Velocity.ShouldHaveSingleItem().Points.ShouldBe(3);

        var lastSprints = await SummaryAsync(owner, project, OverviewPeriod.LastThreeSprints);
        lastSprints.Tickets.Total.ShouldBe(2);
        lastSprints.Activity.Completed.ShouldBe(2);
        lastSprints.VelocitySprints.ShouldBe(2);
        lastSprints.Velocity.ShouldHaveSingleItem().Points.ShouldBe(8);
    }

    [Fact]
    public async Task Summary_is_for_readers_of_the_space_only()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var stranger = await fixture.CreateUserClientAsync(Token);

        (await stranger.Http.GetAsync($"/api/v1/project-overview/summary/{project.Id}", Token)).IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Introduction_is_written_by_space_admins_and_read_by_members()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var member = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{project.Id}/member", new { userId = member.UserId, role = ProjectRoleNames.Member }, Token)).EnsureSuccessStatusCode();
        var path = $"/api/v1/project-introduction?projectId={project.Id}";

        var empty = (await member.Http.GetFromJsonAsync<ProjectIntroductionDto>(path, Token))!;
        empty.Content.ShouldBeEmpty();
        empty.UpdatedAt.ShouldBeNull();
        empty.CanEdit.ShouldBeFalse();

        var refused = await member.Http.PutAsJsonAsync(path, new { content = "<p>Mine</p>" }, Token);
        refused.IsSuccessStatusCode.ShouldBeFalse();

        var saved = await owner.Http.PutAsJsonAsync(path, new { content = "<h2>About</h2><p>Hello</p><script>alert(1)</script>" }, Token);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        (await owner.Http.PutAsJsonAsync(path, new { content = "<h2>About</h2><p>Hello again</p>" }, Token)).EnsureSuccessStatusCode();

        var read = (await member.Http.GetFromJsonAsync<ProjectIntroductionDto>(path, Token))!;
        read.Content.ShouldContain("Hello again");
        read.Content.ShouldNotContain("script");
        read.UpdatedBy!.Id.ShouldBe(owner.UserId);
        read.UpdatedAt.ShouldNotBeNull();
        (await owner.Http.GetFromJsonAsync<ProjectIntroductionDto>(path, Token))!.CanEdit.ShouldBeTrue();
    }
}
