using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Agile;
using SprintBoot.Issues;
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

    private static async Task<IssueDto> CreateIssueAsync(AuthClient client, ProjectDto project, string typeName, object? assigneeId = null, decimal? points = null, Guid? parentId = null)
    {
        var types = await client.Http.GetFromJsonAsync<List<IssueTypeDto>>($"/api/v1/issue/types/{project.Id}", Token);
        var typeId = types!.Single(type => type.Name == typeName).Id;
        var response = await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = project.Id, typeId, title = $"{typeName} work", assigneeId, storyPoints = points, parentId }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<IssueDto>(Token))!;
    }

    private static async Task FinishAsync(AuthClient client, IssueDto issue)
    {
        var reloaded = await client.Http.GetFromJsonAsync<IssueDto>($"/api/v1/issue/by-key?key={issue.Key}", Token);
        var available = await client.Http.GetFromJsonAsync<List<AvailableTransitionDto>>($"/api/v1/transition/available/{issue.Id}", Token);
        var done = available!.Single(item => item.ToStatus.Category == StatusCategory.Done);
        (await client.Http.PostAsJsonAsync(
            $"/api/v1/transition/execute/{issue.Id}",
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
    public async Task Summary_counts_the_issues_of_the_sprint_and_velocity_of_closed_sprints()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var finished = await CreateIssueAsync(owner, project, "Task", owner.UserId, 5);
        var open = await CreateIssueAsync(owner, project, "Task", owner.UserId, 3);
        await CreateIssueAsync(owner, project, "Bug");
        await CreateIssueAsync(owner, project, "Epic");
        await CreateIssueAsync(owner, project, "Subtask", parentId: open.Id);

        var sprintResponse = await owner.Http.PostAsJsonAsync("/api/v1/sprint", new { projectId = project.Id, name = "Sprint 1" }, Token);
        var sprint = (await sprintResponse.Content.ReadFromJsonAsync<SprintSummaryDto>(Token))!;
        foreach (var issue in new[] { finished, open })
        {
            (await owner.Http.PostAsJsonAsync($"/api/v1/sprint/move-issue/{issue.Id}", new { sprintId = sprint.Id }, Token)).EnsureSuccessStatusCode();
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

        // Only the sprint's issues and their subtasks count: the bug and the epic outside it do not.
        running.Issues.Total.ShouldBe(3);
        running.Issues.CreatedInPeriod.ShouldBe(3);
        running.Issues.Done.ShouldBe(1);
        running.Issues.ToDo.ShouldBe(2);
        running.ByType.Select(item => (item.Name, item.Count)).ShouldBe([("Task", 2), ("Subtask", 1)]);
        running.Activity.Completed.ShouldBe(1);
        running.Activity.LastCompletedAt.ShouldNotBeNull();
        running.PullRequests.Total.ShouldBe(0);

        // The running sprint counts at its progress so far: the finished task's points, not the open one's.
        running.VelocitySprints.ShouldBe(1);
        var progress = running.Velocity.ShouldHaveSingleItem();
        progress.User.Id.ShouldBe(owner.UserId);
        progress.Points.ShouldBe(5);
        progress.Issues.ShouldBe(1);
        running.Recent.ShouldContain(item => item.Kind == IssueEventKind.Changed && item.Field == "status" && item.IssueKey == finished.Key);
        running.Recent.Count.ShouldBe(5);

        (await owner.Http.PostAsJsonAsync($"/api/v1/sprint/{sprint.Id}/complete", new { toBacklog = true }, Token)).EnsureSuccessStatusCode();

        var closed = await SummaryAsync(owner, project, OverviewPeriod.ThisYear);
        closed.Range.From.ShouldBe(new DateOnly(today.Year, 1, 1));
        closed.Range.SprintName.ShouldBeNull();
        closed.Range.BySprint.ShouldBeFalse();
        closed.Issues.Total.ShouldBe(5);
        closed.VelocitySprints.ShouldBe(1);
        var velocity = closed.Velocity.ShouldHaveSingleItem();
        velocity.User.Id.ShouldBe(owner.UserId);
        velocity.Points.ShouldBe(5);
        velocity.Issues.ShouldBe(1);
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
