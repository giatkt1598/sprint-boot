using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using SprintBoot.Agile;
using SprintBoot.Issues;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>Acceptance criteria of the Scrum and Kanban page.</summary>
[Collection(ApiCollection.Name)]
public class AgileTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<ProjectDto> CreateProjectAsync(AuthClient client, int type = 0)
    {
        var key = $"A{Random.Shared.Next(100000, 999999)}";
        var response = await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Agile", key, type }, Token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
    }

    private static async Task<List<IssueDto>> CreateIssuesAsync(AuthClient client, ProjectDto project, int count)
    {
        var types = await client.Http.GetFromJsonAsync<List<IssueTypeDto>>($"/api/v1/issue/types/{project.Id}", Token);
        var typeId = types!.Single(type => type.Name == "Task").Id;
        var result = new List<IssueDto>();
        for (var index = 0; index < count; index++)
        {
            var response = await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = project.Id, typeId, title = $"Issue {index}" }, Token);
            response.EnsureSuccessStatusCode();
            result.Add((await response.Content.ReadFromJsonAsync<IssueDto>(Token))!);
        }

        return result;
    }

    private static async Task<SprintSummaryDto> CreateSprintAsync(AuthClient client, ProjectDto project, string name = "Sprint 1")
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/sprint", new { projectId = project.Id, name }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<SprintSummaryDto>(Token))!;
    }

    private static Task<HttpResponseMessage> StartAsync(AuthClient client, SprintSummaryDto sprint) =>
        client.Http.PostAsJsonAsync(
            $"/api/v1/sprint/{sprint.Id}/start",
            new { name = sprint.Name, startDate = "2026-11-01", endDate = "2026-11-14" },
            Token);

    private static async Task AddAsync(AuthClient client, SprintSummaryDto sprint, IssueDto issue) =>
        (await client.Http.PostAsJsonAsync($"/api/v1/sprint/move-issue/{issue.Id}", new { sprintId = sprint.Id }, Token)).EnsureSuccessStatusCode();

    private static Task<BacklogDto?> BacklogAsync(AuthClient client, ProjectDto project) =>
        client.Http.GetFromJsonAsync<BacklogDto>($"/api/v1/sprint/backlog/{project.Id}", Token);

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

    [Fact]
    public async Task New_issues_append_and_a_move_writes_only_the_moved_rank()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var issues = await CreateIssuesAsync(client, project, 4);

        (await BacklogAsync(client, project))!.Backlog.Select(card => card.Key).ShouldBe(issues.Select(issue => issue.Key));
        var ranksBefore = await fixture.GetRanksAsync(project.Id);

        var response = await client.Http.PostAsJsonAsync(
            $"/api/v1/board/rank/{issues[3].Id}", new { beforeIssueId = issues[0].Id }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Token));

        (await BacklogAsync(client, project))!.Backlog.First().Key.ShouldBe(issues[3].Key);
        var ranksAfter = await fixture.GetRanksAsync(project.Id);
        ranksAfter.Count(pair => ranksBefore[pair.Key] != pair.Value).ShouldBe(1);
        ranksAfter[issues[3].Id].ShouldNotBe(ranksBefore[issues[3].Id]);
    }

    [Fact]
    public async Task Second_active_sprint_is_refused()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var first = await CreateSprintAsync(client, project);
        var second = await CreateSprintAsync(client, project, "Sprint 2");

        (await StartAsync(client, first)).EnsureSuccessStatusCode();
        var response = await StartAsync(client, second);

        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(AgileErrors.ActiveSprintExists);
    }

    [Fact]
    public async Task Start_requires_name_and_both_dates()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var sprint = await CreateSprintAsync(client, project);

        var response = await client.Http.PostAsJsonAsync($"/api/v1/sprint/{sprint.Id}/start", new { name = sprint.Name }, Token);

        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(AgileErrors.SprintDetailsRequired);
    }

    [Fact]
    public async Task Complete_requires_a_destination_and_keeps_the_closed_sprint_readable()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var issues = await CreateIssuesAsync(client, project, 2);
        var sprint = await CreateSprintAsync(client, project);
        var next = await CreateSprintAsync(client, project, "Sprint 2");
        await AddAsync(client, sprint, issues[0]);
        await AddAsync(client, sprint, issues[1]);
        (await StartAsync(client, sprint)).EnsureSuccessStatusCode();
        await FinishAsync(client, issues[0]);

        var refused = await client.Http.PostAsJsonAsync($"/api/v1/sprint/{sprint.Id}/complete", new { }, Token);
        (await refused.Content.ReadAsStringAsync(Token)).ShouldContain(AgileErrors.CompletionTargetRequired);

        (await client.Http.PostAsJsonAsync($"/api/v1/sprint/{sprint.Id}/complete", new { targetSprintId = next.Id }, Token)).EnsureSuccessStatusCode();

        var closed = await client.Http.GetFromJsonAsync<ClosedSprintDto>($"/api/v1/sprint/{sprint.Id}/closed", Token);
        closed!.Completed.Single().Key.ShouldBe(issues[0].Key);
        closed.NotCompleted.Single().Key.ShouldBe(issues[1].Key);

        var backlog = await BacklogAsync(client, project);
        backlog!.Sprints.Single().Issues.Single().Key.ShouldBe(issues[1].Key);
        backlog.ClosedSprints.Single().Id.ShouldBe(sprint.Id);
    }

    [Fact]
    public async Task Deleting_a_future_sprint_returns_issues_to_the_backlog()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var issues = await CreateIssuesAsync(client, project, 1);
        var sprint = await CreateSprintAsync(client, project);
        await AddAsync(client, sprint, issues[0]);
        (await BacklogAsync(client, project))!.Backlog.ShouldBeEmpty();

        (await client.Http.DeleteAsync($"/api/v1/sprint/{sprint.Id}", Token)).EnsureSuccessStatusCode();

        (await BacklogAsync(client, project))!.Backlog.Single().Key.ShouldBe(issues[0].Key);
    }

    [Fact]
    public async Task Adding_to_an_active_sprint_is_recorded_in_activity()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var issues = await CreateIssuesAsync(client, project, 1);
        var sprint = await CreateSprintAsync(client, project);
        (await StartAsync(client, sprint)).EnsureSuccessStatusCode();

        await AddAsync(client, sprint, issues[0]);

        var activity = await client.Http.GetFromJsonAsync<List<IssueActivityDto>>($"/api/v1/issue/{issues[0].Id}/activity", Token);
        activity!.Single(item => item.Field == "sprint").NewValue.ShouldBe(sprint.Name);
    }

    [Fact]
    public async Task Scrum_board_shows_only_the_active_sprint_and_reports_unmapped_statuses()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var issues = await CreateIssuesAsync(client, project, 2);
        var sprint = await CreateSprintAsync(client, project);
        await AddAsync(client, sprint, issues[0]);
        (await StartAsync(client, sprint)).EnsureSuccessStatusCode();

        var view = await client.Http.GetFromJsonAsync<BoardViewDto>($"/api/v1/board/view/{project.Id}", Token);

        view!.Columns.Select(column => column.Name).ShouldBe(["To Do", "In Progress", "Done"]);
        view.Cards.Single().Key.ShouldBe(issues[0].Key);
        view.UnmappedStatuses.ShouldBeEmpty();

        var columns = view.Columns.Take(2).Select(column => new
        {
            name = column.Name,
            maxLimit = 1,
            statusIds = column.Statuses.Select(status => status.Id).ToArray(),
        });
        var saved = await client.Http.PutAsJsonAsync($"/api/v1/board/columns/{project.Id}", new { columns }, Token);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        var updated = (await saved.Content.ReadFromJsonAsync<BoardViewDto>(Token))!;
        updated.UnmappedStatuses.Single().Name.ShouldBe("Done");
        updated.Columns.First().MaxLimit.ShouldBe(1);
    }

    [Fact]
    public async Task A_status_cannot_sit_in_two_columns()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var view = await client.Http.GetFromJsonAsync<BoardViewDto>($"/api/v1/board/view/{project.Id}", Token);
        var status = view!.Columns[0].Statuses[0].Id;

        var response = await client.Http.PutAsJsonAsync($"/api/v1/board/columns/{project.Id}", new
        {
            columns = new[] { new { name = "A", statusIds = new[] { status } }, new { name = "B", statusIds = new[] { status } } },
        }, Token);

        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(AgileErrors.StatusInTwoColumns);
    }

    [Fact]
    public async Task Kanban_issues_stay_in_the_backlog_until_pulled_onto_the_board()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client, type: 1);
        var issues = await CreateIssuesAsync(client, project, 2);

        (await client.Http.GetFromJsonAsync<BoardViewDto>($"/api/v1/board/view/{project.Id}", Token))!.Cards.ShouldBeEmpty();

        (await client.Http.PostAsJsonAsync($"/api/v1/board/set-entry?projectId={project.Id}&issueId={issues[0].Id}", new { onBoard = true }, Token)).EnsureSuccessStatusCode();

        (await client.Http.GetFromJsonAsync<BoardViewDto>($"/api/v1/board/view/{project.Id}", Token))!.Cards.Single().Key.ShouldBe(issues[0].Key);
        (await BacklogAsync(client, project))!.Backlog.Single().Key.ShouldBe(issues[1].Key);

        var sprint = await client.Http.PostAsJsonAsync("/api/v1/sprint", new { projectId = project.Id, name = "No" }, Token);
        (await sprint.Content.ReadAsStringAsync(Token)).ShouldContain(AgileErrors.KanbanHasNoSprints);
    }

    [Fact]
    public async Task Epics_report_progress_from_their_children()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var types = await client.Http.GetFromJsonAsync<List<IssueTypeDto>>($"/api/v1/issue/types/{project.Id}", Token);
        var epicResponse = await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = project.Id, typeId = types!.Single(type => type.Name == "Epic").Id, title = "Epic" }, Token);
        var epic = (await epicResponse.Content.ReadFromJsonAsync<IssueDto>(Token))!;
        var taskType = types!.Single(type => type.Name == "Task").Id;
        var children = new List<IssueDto>();
        for (var index = 0; index < 2; index++)
        {
            var created = await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = project.Id, typeId = taskType, title = "Child", parentId = epic.Id }, Token);
            children.Add((await created.Content.ReadFromJsonAsync<IssueDto>(Token))!);
        }

        await FinishAsync(client, children[0]);

        var row = (await client.Http.GetFromJsonAsync<List<EpicRowDto>>($"/api/v1/issue/epics/{project.Id}", Token))!.Single();
        row.Total.ShouldBe(2);
        row.Done.ShouldBe(1);
    }

    [Fact]
    public async Task Outsider_cannot_read_a_board_or_join_its_realtime_group()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var outsider = await fixture.CreateUserClientAsync(Token);

        (await outsider.Http.GetAsync($"/api/v1/board/view/{project.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.Http.GetAsync($"/api/v1/sprint/backlog/{project.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var connection = fixture.CreateBoardConnection(outsider);
        await connection.StartAsync(Token);
        await Should.ThrowAsync<HubException>(() => connection.InvokeAsync("Join", project.Id, Token));
    }

    [Fact]
    public async Task Board_members_get_a_realtime_signal_after_a_transition()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var issues = await CreateIssuesAsync(client, project, 1);

        await using var connection = fixture.CreateBoardConnection(client);
        var signal = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<Guid>("BoardChanged", projectId => signal.TrySetResult(projectId));
        await connection.StartAsync(Token);
        await connection.InvokeAsync("Join", project.Id, Token);

        await FinishAsync(client, issues[0]);

        (await signal.Task.WaitAsync(TimeSpan.FromSeconds(30), Token)).ShouldBe(project.Id);
    }

    [Fact]
    public async Task Viewer_cannot_plan()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var issues = await CreateIssuesAsync(owner, project, 1);
        var viewer = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{project.Id}/member", new { userId = viewer.UserId, role = ProjectRoleNames.Viewer }, Token)).EnsureSuccessStatusCode();

        (await viewer.Http.PostAsJsonAsync($"/api/v1/board/rank/{issues[0].Id}", new { }, Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.Http.PostAsJsonAsync("/api/v1/sprint", new { projectId = project.Id, name = "X" }, Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Search_results_name_the_sprint_and_the_reporter()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var issues = await CreateIssuesAsync(client, project, 2);
        var sprint = await (await client.Http.PostAsJsonAsync($"/api/v1/sprint", new { projectId = project.Id, name = "Named sprint" }, Token)).Content.ReadFromJsonAsync<SprintSummaryDto>(Token);
        (await client.Http.PostAsJsonAsync($"/api/v1/sprint/move-issue/{issues[0].Id}", new { sprintId = sprint!.Id }, Token)).EnsureSuccessStatusCode();

        var found = await client.Http.GetFromJsonAsync<IssueSearchResultDto>($"/api/v1/issue/search?ProjectId={project.Id}", Token);

        found!.Items.Single(item => item.Id == issues[0].Id).SprintName.ShouldBe("Named sprint");
        found.Items.Single(item => item.Id == issues[1].Id).SprintName.ShouldBeNull();
        found.Items.ShouldAllBe(item => item.Reporter != null);
    }

    [Fact]
    public async Task New_sprints_continue_the_name_and_dates_and_later_ones_move_with_an_earlier_one()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        (await client.Http.PutAsJsonAsync($"/api/v1/project/{project.Id}", new { name = "Agile", sprintLengthDays = 21 }, Token)).EnsureSuccessStatusCode();

        async Task<SprintDefaultsDto> DefaultsAsync() =>
            (await client.Http.GetFromJsonAsync<SprintDefaultsDto>($"/api/v1/sprint/new-defaults/{project.Id}", Token))!;

        async Task<SprintSummaryDto> CreateAsync(SprintDefaultsDto defaults) =>
            (await (await client.Http.PostAsJsonAsync("/api/v1/sprint", new { projectId = project.Id, name = defaults.Name, startDate = defaults.StartDate, endDate = defaults.EndDate }, Token)).Content.ReadFromJsonAsync<SprintSummaryDto>(Token))!;

        var first = await DefaultsAsync();
        first.Name.ShouldBe("Sprint 1");
        first.EndDate.ShouldBe(first.StartDate.AddDays(20));
        var one = await CreateAsync(first);

        var second = await DefaultsAsync();
        second.Name.ShouldBe("Sprint 2");
        second.StartDate.ShouldBe(first.EndDate.AddDays(1));
        var two = await CreateAsync(second);

        var moved = first.EndDate.AddDays(5);
        (await client.Http.PutAsJsonAsync($"/api/v1/sprint/{one.Id}", new { name = one.Name, startDate = first.StartDate, endDate = moved }, Token)).EnsureSuccessStatusCode();

        var backlog = await client.Http.GetFromJsonAsync<BacklogDto>($"/api/v1/sprint/backlog/{project.Id}", Token);
        var shifted = backlog!.Sprints.Single(item => item.Sprint.Id == two.Id).Sprint;
        shifted.StartDate.ShouldBe(second.StartDate.AddDays(5));
        shifted.EndDate.ShouldBe(second.EndDate.AddDays(5));
    }

    [Fact]
    public async Task Board_columns_can_be_hidden_and_cards_choose_their_fields()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var view = await client.Http.GetFromJsonAsync<BoardViewDto>($"/api/v1/board/view/{project.Id}", Token);
        view!.CardFields.ShouldBe(BoardCardFields.Default);
        var columns = view.Columns.Select((column, index) => new { name = column.Name, isHidden = index == 0, statusIds = column.Statuses.Select(status => status.Id).ToArray() }).ToArray();

        var saved = await client.Http.PutAsJsonAsync($"/api/v1/board/columns/{project.Id}", new { columns, cardFields = new[] { "epic", "nonsense", "key" } }, Token);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        var result = (await saved.Content.ReadFromJsonAsync<BoardViewDto>(Token))!;
        result.Columns[0].IsHidden.ShouldBeTrue();
        result.CardFields.ShouldBe(["key", "epic"]);

        // Saving without a field list, as older callers do, leaves it alone.
        var kept = await client.Http.PutAsJsonAsync($"/api/v1/board/columns/{project.Id}", new { columns }, Token);
        (await kept.Content.ReadFromJsonAsync<BoardViewDto>(Token))!.CardFields.ShouldBe(["key", "epic"]);
    }
}
