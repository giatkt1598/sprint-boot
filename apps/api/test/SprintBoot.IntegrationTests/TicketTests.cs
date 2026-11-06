using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SprintBoot.Issues;
using SprintBoot.Projects;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>One test per acceptance criterion of the Ticket page, plus permission rules.</summary>
[Collection(ApiCollection.Name)]
public class TicketTests(ApiFixture fixture)
{
    private static int _projectCounter;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string NewKey() => $"T{Interlocked.Increment(ref _projectCounter)}{Random.Shared.Next(1000, 9999)}";

    private async Task<ProjectDto> CreateProjectAsync(AuthClient client)
    {
        var response = await client.Http.PostAsJsonAsync(
            "/api/v1/project", new { name = "Test", key = NewKey(), type = 0 }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
    }

    private static async Task<Guid> TypeIdAsync(AuthClient client, Guid projectId, string name)
    {
        var types = await client.Http.GetFromJsonAsync<List<IssueTypeDto>>(
            $"/api/v1/issue/types/{projectId}", Token);
        return types!.Single(type => type.Name == name).Id;
    }

    private static async Task<HttpResponseMessage> PostIssueAsync(
        AuthClient client, Guid projectId, Guid typeId, string title = "Issue", Guid? parentId = null) =>
        await client.Http.PostAsJsonAsync(
            "/api/v1/issue", new { projectId, typeId, title, parentId }, Token);

    private static async Task<IssueDto> CreateIssueAsync(AuthClient client, ProjectDto project, string title = "Issue")
    {
        var response = await PostIssueAsync(client, project.Id, await TypeIdAsync(client, project.Id, "Task"), title);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<IssueDto>(Token))!;
    }

    private static Task<HttpResponseMessage> PatchAsync(AuthClient client, IssueDto issue, object fields) =>
        client.Http.PutAsJsonAsync(
            $"/api/v1/issue/{issue.Id}", new { concurrencyStamp = issue.ConcurrencyStamp, fields }, Token);

    [Fact]
    public async Task Concurrent_creates_get_distinct_consecutive_keys()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var typeId = await TypeIdAsync(client, project.Id, "Task");

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostIssueAsync(client, project.Id, typeId)));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
        var keys = new List<string>();
        foreach (var response in responses)
        {
            keys.Add((await response.Content.ReadFromJsonAsync<IssueDto>(Token))!.Key);
        }

        keys.Distinct().Count().ShouldBe(20);
        keys.ShouldBe(Enumerable.Range(1, 20).Select(number => $"{project.Key}-{number}"), ignoreOrder: true);
    }

    [Fact]
    public async Task Subtask_without_parent_is_rejected()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);

        var response = await PostIssueAsync(client, project.Id, await TypeIdAsync(client, project.Id, "Subtask"));

        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(IssueErrors.ParentRequired);
    }

    [Fact]
    public async Task Update_api_cannot_change_status()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var issue = await CreateIssueAsync(client, await CreateProjectAsync(client));

        var response = await PatchAsync(client, issue, new { status = Guid.NewGuid() });

        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(IssueErrors.FieldNotWritable);
    }

    [Fact]
    public async Task Each_field_change_writes_exactly_one_activity_row()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var issue = await CreateIssueAsync(client, await CreateProjectAsync(client));

        (await PatchAsync(client, issue, new { title = "Renamed", storyPoints = 3, labels = new[] { "api" } }))
            .EnsureSuccessStatusCode();

        var activity = await client.Http.GetFromJsonAsync<List<IssueActivityDto>>($"/api/v1/issue/{issue.Id}/activity", Token);
        activity!.Select(item => item.Field).ShouldBe(["title", "storyPoints", "labels"], ignoreOrder: true);
        activity.Single(item => item.Field == "title").OldValue.ShouldBe("Issue");
        activity.Single(item => item.Field == "title").NewValue.ShouldBe("Renamed");
    }

    [Fact]
    public async Task History_shows_names_for_people_priorities_and_parents()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var created = await PostIssueAsync(client, project.Id, await TypeIdAsync(client, project.Id, "Epic"), "Parent");
        var parent = (await created.Content.ReadFromJsonAsync<IssueDto>(Token))!;
        var issue = await CreateIssueAsync(client, project);
        var priorities = (await client.Http.GetFromJsonAsync<List<PriorityDto>>("/api/v1/issue/priorities", Token))!;
        var high = priorities.Single(item => item.Name == "High");

        var patched = await PatchAsync(client, issue, new { assignee = client.UserId, priority = high.Id, parent = parent.Id });
        patched.StatusCode.ShouldBe(HttpStatusCode.OK, await patched.Content.ReadAsStringAsync(Token));

        var activity = (await client.Http.GetFromJsonAsync<List<IssueActivityDto>>($"/api/v1/issue/{issue.Id}/activity", Token))!;
        activity.Single(item => item.Field == "assignee").NewValue.ShouldEndWith("@test.local");
        activity.Single(item => item.Field == "priority").OldValue.ShouldBe("Medium");
        activity.Single(item => item.Field == "priority").NewValue.ShouldBe("High");
        activity.Single(item => item.Field == "parent").NewValue.ShouldBe($"{parent.Key} Parent");
    }

    [Fact]
    public async Task Saving_the_same_value_writes_no_activity()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var issue = await CreateIssueAsync(client, await CreateProjectAsync(client));

        (await PatchAsync(client, issue, new { title = issue.Title })).EnsureSuccessStatusCode();

        var activity = await client.Http.GetFromJsonAsync<List<IssueActivityDto>>($"/api/v1/issue/{issue.Id}/activity", Token);
        activity.ShouldBeEmpty();
    }

    [Fact]
    public async Task Outsider_cannot_read_an_issue_by_url_or_search()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var issue = await CreateIssueAsync(owner, project, "Secret roadmap");
        var outsider = await fixture.CreateUserClientAsync(Token);

        (await outsider.Http.GetAsync($"/api/v1/issue/by-key?key={issue.Key}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.Http.GetAsync($"/api/v1/issue/{issue.Id}/activity", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.Http.GetAsync($"/api/v1/project/{project.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var byText = await outsider.Http.GetFromJsonAsync<IssueSearchResultDto>("/api/v1/issue/search?text=roadmap", Token);
        byText!.TotalCount.ShouldBe(0);

        var byKey = await outsider.Http.GetFromJsonAsync<IssueSearchResultDto>($"/api/v1/issue/search?text={issue.Key}", Token);
        byKey!.ExactKey.ShouldBeNull();
        byKey.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Exact_issue_key_resolves_directly_and_text_search_finds_words()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var issue = await CreateIssueAsync(client, project, "Tối ưu đăng nhập");

        var byKey = await client.Http.GetFromJsonAsync<IssueSearchResultDto>(
            $"/api/v1/issue/search?text={issue.Key.ToLowerInvariant()}", Token);
        byKey!.ExactKey.ShouldBe(issue.Key);

        var byText = await client.Http.GetFromJsonAsync<IssueSearchResultDto>(
            $"/api/v1/issue/search?projectId={project.Id}&text=đăng nhập", Token);
        byText!.Items.Single().Key.ShouldBe(issue.Key);
    }

    [Fact]
    public async Task Search_sorts_by_the_requested_column_in_either_direction()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var banana = await CreateIssueAsync(client, project, "Banana");
        await CreateIssueAsync(client, project, "apple");
        var cherry = await CreateIssueAsync(client, project, "Cherry");

        var priorities = (await client.Http.GetFromJsonAsync<List<PriorityDto>>("/api/v1/issue/priorities", Token))!
            .OrderBy(priority => priority.Order).ToList();
        (await PatchAsync(client, cherry, new { priority = priorities[0].Id })).EnsureSuccessStatusCode();
        (await PatchAsync(client, banana, new { priority = priorities[^1].Id, assignee = client.UserId })).EnsureSuccessStatusCode();

        async Task<List<string>> TitlesAsync(string sorting)
        {
            var result = await client.Http.GetFromJsonAsync<IssueSearchResultDto>(
                $"/api/v1/issue/search?projectId={project.Id}&sorting={Uri.EscapeDataString(sorting)}", Token);
            return [.. result!.Items.Select(item => item.Title)];
        }

        (await TitlesAsync("title")).ShouldBe(["apple", "Banana", "Cherry"]);
        (await TitlesAsync("title desc")).ShouldBe(["Cherry", "Banana", "apple"]);
        (await TitlesAsync("key")).ShouldBe(["Banana", "apple", "Cherry"]);
        (await TitlesAsync("key desc")).ShouldBe(["Cherry", "apple", "Banana"]);
        (await TitlesAsync("priority"))[0].ShouldBe("Cherry");
        (await TitlesAsync("priority desc"))[0].ShouldBe("Banana");
        // Nobody assigned sorts after everyone.
        (await TitlesAsync("assignee"))[0].ShouldBe("Banana");
        (await TitlesAsync("assignee desc"))[0].ShouldBe("Banana");
        (await TitlesAsync("created desc")).ShouldBe(["Cherry", "apple", "Banana"]);

        var invalid = await client.Http.GetAsync($"/api/v1/issue/search?projectId={project.Id}&sorting=description", Token);
        invalid.IsSuccessStatusCode.ShouldBeFalse();
        (await invalid.Content.ReadAsStringAsync(Token)).ShouldContain(IssueErrors.InvalidSorting);
    }

    [Fact]
    public async Task Oversized_attachment_is_rejected_with_a_clear_error()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var issue = await CreateIssueAsync(client, await CreateProjectAsync(client));
        var file = new SampleFile(2 * 1024 * 1024);

        var response = await client.Http.PostAsync($"/api/v1/attachment/upload/{issue.Id}", file.Content, Token);

        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(IssueErrors.AttachmentTooLarge);
    }

    [Fact]
    public async Task Attachment_round_trips_and_outsiders_cannot_download()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var issue = await CreateIssueAsync(client, await CreateProjectAsync(client));

        var response = await client.Http.PostAsync($"/api/v1/attachment/upload/{issue.Id}", new SampleFile(1024).Content, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        var attachment = (await response.Content.ReadFromJsonAsync<AttachmentDto>(Token))!;

        var bytes = await client.Http.GetByteArrayAsync($"/api/v1/attachment/{attachment.Id}/download", Token);
        bytes.Length.ShouldBe(1024);

        var outsider = await fixture.CreateUserClientAsync(Token);
        (await outsider.Http.GetAsync($"/api/v1/attachment/{attachment.Id}/download", Token))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Work_log_totals_update_and_zero_minutes_is_rejected()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var issue = await CreateIssueAsync(client, await CreateProjectAsync(client));
        var workDate = "2026-11-01";

        (await client.Http.PostAsJsonAsync("/api/v1/work-log", new { issueId = issue.Id, minutes = 90, workDate }, Token)).EnsureSuccessStatusCode();
        (await client.Http.PostAsJsonAsync("/api/v1/work-log", new { issueId = issue.Id, minutes = 30, workDate }, Token)).EnsureSuccessStatusCode();
        var zero = await client.Http.PostAsJsonAsync("/api/v1/work-log", new { issueId = issue.Id, minutes = 0, workDate }, Token);

        (await zero.Content.ReadAsStringAsync(Token)).ShouldContain(IssueErrors.WorkLogInvalidTime);
        var list = await client.Http.GetFromJsonAsync<WorkLogListDto>($"/api/v1/work-log?issueId={issue.Id}", Token);
        list!.TotalMinutes.ShouldBe(120);
    }

    [Fact]
    public async Task Stale_concurrency_stamp_returns_409()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var issue = await CreateIssueAsync(client, await CreateProjectAsync(client));

        (await PatchAsync(client, issue, new { title = "First" })).EnsureSuccessStatusCode();
        var stale = await PatchAsync(client, issue, new { title = "Second" });

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dangerous_html_is_cleaned_and_plain_text_is_kept()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var issue = await CreateIssueAsync(client, await CreateProjectAsync(client));

        var response = await client.Http.PostAsJsonAsync("/api/v1/comment", new
        {
            issueId = issue.Id,
            body = "<p onclick=\"steal()\">Hello<script>alert(1)</script></p>",
        }, Token);

        var comment = (await response.Content.ReadFromJsonAsync<CommentDto>(Token))!;
        comment.Body.ShouldBe("<p>Hello</p>");
    }

    [Fact]
    public async Task Reply_to_a_reply_is_rejected()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var issue = await CreateIssueAsync(client, await CreateProjectAsync(client));
        var top = await PostCommentAsync(client, issue.Id, null);
        var reply = await PostCommentAsync(client, issue.Id, top.Id);

        var nested = await client.Http.PostAsJsonAsync(
            "/api/v1/comment", new { issueId = issue.Id, parentId = reply.Id, body = "<p>x</p>" }, Token);

        (await nested.Content.ReadAsStringAsync(Token)).ShouldContain(IssueErrors.ReplyDepth);
    }

    private static async Task<CommentDto> PostCommentAsync(AuthClient client, Guid issueId, Guid? parentId)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/comment", new { issueId, parentId, body = "<p>x</p>" }, Token);
        return (await response.Content.ReadFromJsonAsync<CommentDto>(Token))!;
    }

    [Fact]
    public async Task Viewer_can_read_but_not_edit_or_comment()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var issue = await CreateIssueAsync(owner, project);
        var viewer = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync(
            $"/api/v1/project/{project.Id}/member", new { userId = viewer.UserId, role = ProjectRoleNames.Viewer }, Token))
            .EnsureSuccessStatusCode();

        (await viewer.Http.GetAsync($"/api/v1/issue/by-key?key={issue.Key}", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PatchAsync(viewer, issue, new { title = "Nope" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.Http.PostAsJsonAsync("/api/v1/comment", new { issueId = issue.Id, body = "<p>x</p>" }, Token))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_deletes_own_issue_only_and_admin_restores_with_subtasks()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var member = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync(
            $"/api/v1/project/{project.Id}/member", new { userId = member.UserId, role = ProjectRoleNames.Member }, Token))
            .EnsureSuccessStatusCode();

        var ownersIssue = await CreateIssueAsync(owner, project);
        var membersIssue = await CreateIssueAsync(member, project);
        var subtaskResponse = await PostIssueAsync(
            member, project.Id, await TypeIdAsync(member, project.Id, "Subtask"), "Sub", membersIssue.Id);
        var subtask = (await subtaskResponse.Content.ReadFromJsonAsync<IssueDto>(Token))!;

        (await member.Http.DeleteAsync($"/api/v1/issue/{ownersIssue.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.Http.DeleteAsync($"/api/v1/issue/{membersIssue.Id}", Token)).EnsureSuccessStatusCode();
        (await member.Http.GetAsync($"/api/v1/issue/by-key?key={subtask.Key}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await member.Http.PostAsync($"/api/v1/issue/{membersIssue.Id}/restore", null, Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        var deleted = await admin.Http.GetFromJsonAsync<JsonDocument>("/api/v1/issue/deleted-list?maxResultCount=50", Token);
        var row = deleted!.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == membersIssue.Id);
        row.GetProperty("subtaskCount").GetInt32().ShouldBe(1);

        (await admin.Http.PostAsync($"/api/v1/issue/{membersIssue.Id}/restore", null, Token)).EnsureSuccessStatusCode();
        (await member.Http.GetAsync($"/api/v1/issue/by-key?key={subtask.Key}", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleted_key_is_never_reused()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(client);
        var first = await CreateIssueAsync(client, project);

        (await client.Http.DeleteAsync($"/api/v1/issue/{first.Id}", Token)).EnsureSuccessStatusCode();
        var second = await CreateIssueAsync(client, project);

        second.Key.ShouldBe($"{project.Key}-2");
    }

    private sealed class SampleFile(int size)
    {
        public MultipartFormDataContent Content { get; } = Build(size);

        private static MultipartFormDataContent Build(int size)
        {
            var file = new ByteArrayContent(new byte[size]);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return new MultipartFormDataContent { { file, "file", "report.pdf" } };
        }
    }
}
