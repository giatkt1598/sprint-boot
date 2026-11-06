using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Issues;
using SprintBoot.Projects;
using Xunit;

namespace SprintBoot.IntegrationTests;

[Collection(ApiCollection.Name)]
public class IssueTemplateTests(ApiFixture fixture)
{
    private static int _projectCounter;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string NewKey() => $"M{Interlocked.Increment(ref _projectCounter)}{Random.Shared.Next(1000, 9999)}";

    private static async Task<ProjectDto> CreateProjectAsync(AuthClient client)
    {
        var response = await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Templates", key = NewKey(), type = 0 }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
    }

    private static async Task<Guid> TypeIdAsync(AuthClient client, Guid projectId, string name)
    {
        var types = await client.Http.GetFromJsonAsync<List<IssueTypeDto>>($"/api/v1/issue/types/{projectId}", Token);
        return types!.Single(type => type.Name == name).Id;
    }

    private static Task<HttpResponseMessage> CreateAsync(AuthClient client, Guid projectId, object body) =>
        client.Http.PostAsJsonAsync($"/api/v1/issue-template?projectId={projectId}", body, Token);

    private static async Task<IssueTemplateDto> CreateTemplateAsync(AuthClient client, Guid projectId, object body)
    {
        var response = await CreateAsync(client, projectId, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<IssueTemplateDto>(Token))!;
    }

    private static async Task<List<IssueTemplateDto>> ListAsync(AuthClient client, Guid projectId) =>
        (await client.Http.GetFromJsonAsync<List<IssueTemplateDto>>($"/api/v1/issue-template?projectId={projectId}", Token))!;

    [Fact]
    public async Task One_default_per_type_and_content_is_sanitized()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var bug = await TypeIdAsync(owner, project.Id, "Bug");
        var story = await TypeIdAsync(owner, project.Id, "Story");

        var first = await CreateTemplateAsync(owner, project.Id, new { issueTypeId = bug, name = "Bug report", content = "<h2>Steps</h2><script>x()</script>", isDefault = true });
        first.Content.ShouldContain("<h2>Steps</h2>");
        first.Content.ShouldNotContain("script");

        var second = await CreateTemplateAsync(owner, project.Id, new { issueTypeId = bug, name = "Crash", content = "<p>Stack</p>", isDefault = true });
        await CreateTemplateAsync(owner, project.Id, new { issueTypeId = story, name = "Story", content = "<p>As a</p>", isDefault = true });

        var list = await ListAsync(owner, project.Id);
        list.Count.ShouldBe(3);
        list.Where(item => item.IssueTypeId == bug && item.IsDefault).ShouldHaveSingleItem().Id.ShouldBe(second.Id);
        list.Single(item => item.IssueTypeId == story).IsDefault.ShouldBeTrue();

        (await owner.Http.DeleteAsync($"/api/v1/issue-template/{first.Id}", Token)).EnsureSuccessStatusCode();
        (await ListAsync(owner, project.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Readers_see_templates_but_only_space_admins_change_them()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var task = await TypeIdAsync(owner, project.Id, "Task");
        var template = await CreateTemplateAsync(owner, project.Id, new { issueTypeId = task, name = "Task", content = "<p>Done when</p>", isDefault = true });

        var member = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{project.Id}/member", new { userId = member.UserId, role = ProjectRoleNames.Member }, Token))
            .EnsureSuccessStatusCode();

        (await ListAsync(member, project.Id)).ShouldHaveSingleItem().Id.ShouldBe(template.Id);
        (await CreateAsync(member, project.Id, new { issueTypeId = task, name = "Mine", content = "", isDefault = false })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.Http.PutAsJsonAsync($"/api/v1/issue-template/{template.Id}", new { issueTypeId = task, name = "Mine", content = "", isDefault = false }, Token))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.Http.DeleteAsync($"/api/v1/issue-template/{template.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var outsider = await fixture.CreateUserClientAsync(Token);
        (await outsider.Http.GetAsync($"/api/v1/issue-template?projectId={project.Id}", Token)).IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Subtasks_and_other_spaces_types_are_refused()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var other = await CreateProjectAsync(owner);

        var subtask = await CreateAsync(owner, project.Id, new { issueTypeId = await TypeIdAsync(owner, project.Id, "Subtask"), name = "Sub", content = "", isDefault = false });
        (await subtask.Content.ReadAsStringAsync(Token)).ShouldContain(IssueTemplateErrors.SubtaskType);

        var foreign = await CreateAsync(owner, project.Id, new { issueTypeId = await TypeIdAsync(owner, other.Id, "Task"), name = "Foreign", content = "", isDefault = false });
        (await foreign.Content.ReadAsStringAsync(Token)).ShouldContain(IssueErrors.ProjectMismatch);

        var blank = await CreateAsync(owner, project.Id, new { issueTypeId = await TypeIdAsync(owner, project.Id, "Task"), name = "  ", content = "", isDefault = false });
        blank.IsSuccessStatusCode.ShouldBeFalse();
    }
}
