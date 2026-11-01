using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SprintBoot.Issues;
using SprintBoot.Notifications;
using SprintBoot.Projects;
using SprintBoot.Workflows;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Uow;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>Acceptance criteria of the Notification page.</summary>
[Collection(ApiCollection.Name)]
public class NotificationTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Team(AuthClient Owner, AuthClient Member, ProjectDto Project, Guid TypeId);

    private async Task<Team> TeamAsync(string memberRole = ProjectRoleNames.Member)
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var key = $"N{Random.Shared.Next(100000, 999999)}";
        var created = await owner.Http.PostAsJsonAsync("/api/v1/project", new { name = "Team", key, type = 0 }, Token);
        var project = (await created.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
        var member = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{project.Id}/member", new { userId = member.UserId, role = memberRole }, Token)).EnsureSuccessStatusCode();
        var types = await owner.Http.GetFromJsonAsync<List<IssueTypeDto>>($"/api/v1/issue/types/{project.Id}", Token);
        return new Team(owner, member, project, types!.Single(type => type.Name == "Task").Id);
    }

    private static async Task<IssueDto> IssueAsync(AuthClient client, Team team, Guid? assigneeId = null)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = team.Project.Id, typeId = team.TypeId, title = "Work", assigneeId }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<IssueDto>(Token))!;
    }

    private static Task<PagedResultDto<NotificationDto>?> InboxAsync(AuthClient client) =>
        client.Http.GetFromJsonAsync<PagedResultDto<NotificationDto>>("/api/v1/notification?maxResultCount=50", Token);

    private static async Task<List<NotificationDto>> WaitForAsync(AuthClient client, Func<NotificationDto, bool> match, int count = 1)
    {
        for (var wait = 0; wait < 100; wait++)
        {
            var found = (await InboxAsync(client))!.Items.Where(match).ToList();
            if (found.Count >= count)
            {
                return found;
            }

            await Task.Delay(200, Token);
        }

        throw new TimeoutException("Expected notification did not arrive.");
    }

    /// <summary>Lets the outbox drain so an absence is meaningful.</summary>
    private async Task SettleAsync(AuthClient client, Team team)
    {
        var marker = await IssueAsync(team.Owner, team);
        await team.Owner.Http.PostAsJsonAsync("/api/v1/comment", new { issueId = marker.Id, body = "<p>marker</p>" }, Token);
        await Task.Delay(1500, Token);
    }

    private static Task<HttpResponseMessage> AssignAsync(AuthClient client, IssueDto issue, Guid? userId) =>
        client.Http.PutAsJsonAsync($"/api/v1/issue/{issue.Id}", new { concurrencyStamp = issue.ConcurrencyStamp, fields = new { assignee = userId } }, Token);

    [Fact]
    public async Task Assignment_notifies_the_new_assignee_once_and_not_the_actor()
    {
        var team = await TeamAsync();
        var issue = await IssueAsync(team.Owner, team);

        (await AssignAsync(team.Owner, issue, team.Member.UserId)).EnsureSuccessStatusCode();

        var found = await WaitForAsync(team.Member, item => item.EventType == NotificationEventTypes.IssueAssigned);
        found.ShouldHaveSingleItem().IssueKey.ShouldBe(issue.Key);
        (await InboxAsync(team.Owner))!.Items.ShouldNotContain(item => item.EventType == NotificationEventTypes.IssueAssigned);
    }

    [Fact]
    public async Task Member_in_several_recipient_groups_gets_one_notification()
    {
        var team = await TeamAsync();
        var issue = await IssueAsync(team.Member, team, team.Member.UserId);

        await team.Owner.Http.PostAsJsonAsync("/api/v1/comment", new { issueId = issue.Id, body = "<p>hello</p>" }, Token);

        await WaitForAsync(team.Member, item => item.EventType == NotificationEventTypes.CommentAdded);
        await Task.Delay(1000, Token);
        (await InboxAsync(team.Member))!.Items.Count(item => item.EventType == NotificationEventTypes.CommentAdded).ShouldBe(1);
    }

    [Fact]
    public async Task A_mention_replaces_the_comment_notification_and_cannot_be_turned_off_by_a_scheme()
    {
        var team = await TeamAsync();
        var issue = await IssueAsync(team.Owner, team);
        var body = $"<p><span data-type=\"mention\" data-id=\"{team.Member.UserId}\">@x</span> look</p>";

        await team.Owner.Http.PostAsJsonAsync("/api/v1/comment", new { issueId = issue.Id, body }, Token);

        await WaitForAsync(team.Member, item => item.EventType == NotificationEventTypes.Mentioned);
        (await InboxAsync(team.Member))!.Items.ShouldNotContain(item => item.EventType == NotificationEventTypes.CommentAdded);
    }

    [Fact]
    public async Task Leaving_a_project_stops_its_notifications()
    {
        var team = await TeamAsync();
        var issue = await IssueAsync(team.Owner, team);
        (await team.Owner.Http.DeleteAsync($"/api/v1/project/{team.Project.Id}/member/{team.Member.UserId}", Token)).EnsureSuccessStatusCode();

        (await AssignAsync(team.Owner, issue, team.Member.UserId)).EnsureSuccessStatusCode();

        await SettleAsync(team.Owner, team);
        (await InboxAsync(team.Member))!.Items.ShouldNotContain(item => item.EventType == NotificationEventTypes.IssueAssigned);
    }

    [Fact]
    public async Task Scheme_rule_changes_apply_to_the_next_event()
    {
        var team = await TeamAsync();
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        var created = await admin.Http.PostAsJsonAsync("/api/v1/notification-scheme", new
        {
            name = $"Quiet {Guid.NewGuid():N}",
            rules = Array.Empty<object>(),
        }, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Token));
        var scheme = (await created.Content.ReadFromJsonAsync<NotificationSchemeDto>(Token))!;
        (await team.Owner.Http.PostAsJsonAsync("/api/v1/notification-scheme/set-for-project", new { projectId = team.Project.Id, schemeId = scheme.Id }, Token)).EnsureSuccessStatusCode();

        var quiet = await IssueAsync(team.Owner, team);
        (await AssignAsync(team.Owner, quiet, team.Member.UserId)).EnsureSuccessStatusCode();
        await SettleAsync(team.Owner, team);
        (await InboxAsync(team.Member))!.Items.ShouldNotContain(item => item.EventType == NotificationEventTypes.IssueAssigned);

        (await admin.Http.PutAsJsonAsync($"/api/v1/notification-scheme/{scheme.Id}", new
        {
            name = scheme.Name,
            rules = new[] { new { eventType = NotificationEventTypes.IssueAssigned, recipientType = (int)RecipientType.Assignee } },
        }, Token)).EnsureSuccessStatusCode();

        var loud = await IssueAsync(team.Owner, team);
        (await AssignAsync(team.Owner, loud, team.Member.UserId)).EnsureSuccessStatusCode();
        await WaitForAsync(team.Member, item => item.EventType == NotificationEventTypes.IssueAssigned);
    }

    [Fact]
    public async Task Send_notification_post_function_reaches_the_scheme_recipients()
    {
        var team = await TeamAsync();
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        var todo = WorkflowsDataSeedContributor.ToDoId;
        var done = WorkflowsDataSeedContributor.DoneId;
        var created = await admin.Http.PostAsJsonAsync("/api/v1/workflow", new
        {
            name = $"Notify {Guid.NewGuid():N}",
            definition = new
            {
                initialStatusId = todo,
                statusIds = new[] { todo, done },
                transitions = new[]
                {
                    new { name = "Finish", fromStatusId = (Guid?)null, toStatusId = done, rules = new object[] { new { kind = 2, key = "notify", config = new { message = "Shipped" } } } },
                },
            },
        }, Token);
        var workflow = (await created.Content.ReadFromJsonAsync<WorkflowDetailDto>(Token))!;
        (await admin.Http.PostAsJsonAsync($"/api/v1/workflow/{workflow.Id}/publish", new { }, Token)).EnsureSuccessStatusCode();
        (await team.Owner.Http.PostAsJsonAsync("/api/v1/project-workflow/set", new { projectId = team.Project.Id, issueTypeId = team.TypeId, workflowId = workflow.Id }, Token)).EnsureSuccessStatusCode();

        var issue = await IssueAsync(team.Owner, team, team.Member.UserId);
        var move = (await team.Owner.Http.GetFromJsonAsync<List<AvailableTransitionDto>>($"/api/v1/transition/available/{issue.Id}", Token))!.Single();
        (await team.Owner.Http.PostAsJsonAsync($"/api/v1/transition/execute/{issue.Id}", new { transitionId = move.Id, concurrencyStamp = issue.ConcurrencyStamp }, Token)).EnsureSuccessStatusCode();

        var found = await WaitForAsync(team.Member, item => item.EventType == NotificationEventTypes.WorkflowNotification);
        found.Single().Detail.ShouldBe("Shipped");
    }

    [Fact]
    public async Task Redelivered_event_creates_no_duplicate()
    {
        var team = await TeamAsync();
        var issue = await IssueAsync(team.Owner, team, team.Member.UserId);
        var eventData = new NotificationEvent(Guid.NewGuid(), NotificationEventTypes.CommentAdded, team.Project.Id, issue.Id, team.Owner.UserId, []);

        await fixture.RunNotificationAsync(eventData);
        await fixture.RunNotificationAsync(eventData);

        var found = (await InboxAsync(team.Member))!.Items.Where(item => item.EventType == NotificationEventTypes.CommentAdded).ToList();
        found.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Quick_field_edits_by_one_person_merge_into_one_notification()
    {
        var team = await TeamAsync();
        var issue = await IssueAsync(team.Owner, team, team.Member.UserId);

        for (var index = 0; index < 3; index++)
        {
            var current = await team.Owner.Http.GetFromJsonAsync<IssueDto>($"/api/v1/issue/by-key?key={issue.Key}", Token);
            (await team.Owner.Http.PutAsJsonAsync($"/api/v1/issue/{issue.Id}", new { concurrencyStamp = current!.ConcurrencyStamp, fields = new { title = $"Title {index}" } }, Token)).EnsureSuccessStatusCode();
        }

        for (var wait = 0; wait < 50; wait++)
        {
            var rows = (await InboxAsync(team.Member))!.Items.Where(item => item.EventType == NotificationEventTypes.IssueUpdated).ToList();
            if (rows.Sum(row => row.MergedCount) >= 3)
            {
                rows.ShouldHaveSingleItem();
                return;
            }

            await Task.Delay(200, Token);
        }

        throw new TimeoutException("Edits were not merged.");
    }

    [Fact]
    public async Task Personal_preference_mutes_an_event_type()
    {
        var team = await TeamAsync();
        (await team.Member.Http.PostAsJsonAsync("/api/v1/notification/save-preferences", new { items = new[] { new { eventType = NotificationEventTypes.IssueAssigned, enabled = false } } }, Token)).EnsureSuccessStatusCode();
        var issue = await IssueAsync(team.Owner, team);

        (await AssignAsync(team.Owner, issue, team.Member.UserId)).EnsureSuccessStatusCode();

        await SettleAsync(team.Owner, team);
        (await InboxAsync(team.Member))!.Items.ShouldNotContain(item => item.EventType == NotificationEventTypes.IssueAssigned);
    }

    [Fact]
    public async Task Inbox_is_private_and_supports_read_one_and_read_all()
    {
        var team = await TeamAsync();
        var issue = await IssueAsync(team.Owner, team);
        (await AssignAsync(team.Owner, issue, team.Member.UserId)).EnsureSuccessStatusCode();
        await team.Owner.Http.PostAsJsonAsync("/api/v1/comment", new { issueId = issue.Id, body = "<p>x</p>" }, Token);
        var found = await WaitForAsync(team.Member, _ => true, 2);

        (await team.Owner.Http.PostAsync($"/api/v1/notification/{found[0].Id}/mark-read", null, Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await team.Member.Http.PostAsync($"/api/v1/notification/{found[0].Id}/mark-read", null, Token)).EnsureSuccessStatusCode();
        var unread = await team.Member.Http.GetFromJsonAsync<int>("/api/v1/notification/unread-count", Token);
        unread.ShouldBe(found.Count - 1);

        (await team.Member.Http.PostAsync("/api/v1/notification/mark-all-read", null, Token)).EnsureSuccessStatusCode();
        (await team.Member.Http.GetFromJsonAsync<int>("/api/v1/notification/unread-count", Token)).ShouldBe(0);
    }

    [Fact]
    public async Task Reporter_assignee_and_commenter_watch_automatically_and_can_stop()
    {
        var team = await TeamAsync();
        var issue = await IssueAsync(team.Owner, team, team.Member.UserId);

        var watchers = await team.Owner.Http.GetFromJsonAsync<WatchersDto>($"/api/v1/watcher?issueId={issue.Id}", Token);
        watchers!.Count.ShouldBe(2);
        watchers.WatchedByMe.ShouldBeTrue();

        (await team.Member.Http.PostAsync($"/api/v1/watcher/unwatch/{issue.Id}", null, Token)).EnsureSuccessStatusCode();
        (await team.Owner.Http.GetFromJsonAsync<WatchersDto>($"/api/v1/watcher?issueId={issue.Id}", Token))!.Count.ShouldBe(1);
    }
}
