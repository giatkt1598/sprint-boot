using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Tickets;
using SprintBoot.Projects;
using Xunit;

namespace SprintBoot.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TicketTemplateTests(ApiFixture fixture)
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
        var types = await client.Http.GetFromJsonAsync<List<TicketTypeDto>>($"/api/v1/ticket/types/{projectId}", Token);
        return types!.Single(type => type.Name == name).Id;
    }

    private static Task<HttpResponseMessage> CreateAsync(AuthClient client, Guid projectId, object body) =>
        client.Http.PostAsJsonAsync($"/api/v1/ticket-template?projectId={projectId}", body, Token);

    private static async Task<TicketTemplateDto> CreateTemplateAsync(AuthClient client, Guid projectId, object body)
    {
        var response = await CreateAsync(client, projectId, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<TicketTemplateDto>(Token))!;
    }

    private static async Task<List<TicketTemplateDto>> ListAsync(AuthClient client, Guid projectId) =>
        (await client.Http.GetFromJsonAsync<List<TicketTemplateDto>>($"/api/v1/ticket-template?projectId={projectId}", Token))!;

    [Fact]
    public async Task One_default_per_type_and_content_is_sanitized()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var bug = await TypeIdAsync(owner, project.Id, "Bug");
        var story = await TypeIdAsync(owner, project.Id, "Story");

        var first = await CreateTemplateAsync(owner, project.Id, new { ticketTypeId = bug, name = "Bug report", content = "<h2>Steps</h2><script>x()</script>", isDefault = true });
        first.Content.ShouldContain("<h2>Steps</h2>");
        first.Content.ShouldNotContain("script");

        var second = await CreateTemplateAsync(owner, project.Id, new { ticketTypeId = bug, name = "Crash", content = "<p>Stack</p>", isDefault = true });
        await CreateTemplateAsync(owner, project.Id, new { ticketTypeId = story, name = "Story", content = "<p>As a</p>", isDefault = true });

        var list = await ListAsync(owner, project.Id);
        list.Count.ShouldBe(3);
        list.Where(item => item.TicketTypeId == bug && item.IsDefault).ShouldHaveSingleItem().Id.ShouldBe(second.Id);
        list.Single(item => item.TicketTypeId == story).IsDefault.ShouldBeTrue();

        (await owner.Http.DeleteAsync($"/api/v1/ticket-template/{first.Id}", Token)).EnsureSuccessStatusCode();
        (await ListAsync(owner, project.Id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Readers_see_templates_but_only_space_admins_change_them()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var task = await TypeIdAsync(owner, project.Id, "Task");
        var template = await CreateTemplateAsync(owner, project.Id, new { ticketTypeId = task, name = "Task", content = "<p>Done when</p>", isDefault = true });

        var member = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{project.Id}/member", new { userId = member.UserId, role = ProjectRoleNames.Member }, Token))
            .EnsureSuccessStatusCode();

        (await ListAsync(member, project.Id)).ShouldHaveSingleItem().Id.ShouldBe(template.Id);
        (await CreateAsync(member, project.Id, new { ticketTypeId = task, name = "Mine", content = "", isDefault = false })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.Http.PutAsJsonAsync($"/api/v1/ticket-template/{template.Id}", new { ticketTypeId = task, name = "Mine", content = "", isDefault = false }, Token))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.Http.DeleteAsync($"/api/v1/ticket-template/{template.Id}", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var outsider = await fixture.CreateUserClientAsync(Token);
        (await outsider.Http.GetAsync($"/api/v1/ticket-template?projectId={project.Id}", Token)).IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Spaces_reuse_workspace_templates_and_follow_their_changes()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        var name = $"Bug report {Guid.NewGuid():N}";
        var created = await admin.Http.PostAsJsonAsync("/api/v1/global-ticket-template", new { ticketTypeKey = "bug", name, content = "<p>Steps</p>", mode = 1 }, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Token));
        var global = (await created.Content.ReadFromJsonAsync<GlobalTicketTemplateDto>(Token))!;
        global.Mode.ShouldBe(TicketTemplateMode.Placeholder);
        global.TicketTypeKey.ShouldBe("bug");
        (await (await admin.Http.PostAsJsonAsync("/api/v1/global-ticket-template", new { ticketTypeKey = "subtask", name, mode = 0 }, Token))
            .Content.ReadAsStringAsync(Token)).ShouldContain(TicketTemplateErrors.InvalidType);

        var owner = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync("/api/v1/global-ticket-template", new { ticketTypeKey = "bug", name = "Mine", content = "", mode = 0 }, Token))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await owner.Http.GetFromJsonAsync<List<GlobalTicketTemplateDto>>("/api/v1/global-ticket-template", Token))!
            .ShouldContain(item => item.Id == global.Id);

        var project = await CreateProjectAsync(owner);
        var bug = await TypeIdAsync(owner, project.Id, "Bug");
        var task = await TypeIdAsync(owner, project.Id, "Task");
        (await (await owner.Http.PostAsJsonAsync($"/api/v1/ticket-template?projectId={project.Id}", new { ticketTypeId = task, globalTemplateId = global.Id }, Token))
            .Content.ReadAsStringAsync(Token)).ShouldContain(TicketTemplateErrors.TypeMismatch);
        var reused = await CreateTemplateAsync(owner, project.Id, new { ticketTypeId = bug, globalTemplateId = global.Id, isDefault = true });
        reused.Name.ShouldBe(name);
        reused.Content.ShouldBe("<p>Steps</p>");
        reused.Mode.ShouldBe(TicketTemplateMode.Placeholder);
        reused.GlobalTemplateId.ShouldBe(global.Id);

        (await admin.Http.PutAsJsonAsync($"/api/v1/global-ticket-template/{global.Id}", new { ticketTypeKey = "bug", name, content = "<p>Steps to reproduce</p>", mode = 0 }, Token))
            .EnsureSuccessStatusCode();
        (await (await admin.Http.PutAsJsonAsync($"/api/v1/global-ticket-template/{global.Id}", new { ticketTypeKey = "task", name, mode = 0 }, Token))
            .Content.ReadAsStringAsync(Token)).ShouldContain(TicketTemplateErrors.TypeInUse);
        var followed = (await ListAsync(owner, project.Id)).ShouldHaveSingleItem();
        followed.Content.ShouldBe("<p>Steps to reproduce</p>");
        followed.Mode.ShouldBe(TicketTemplateMode.Fill);
        (await admin.Http.GetFromJsonAsync<List<GlobalTicketTemplateDto>>("/api/v1/global-ticket-template", Token))!
            .Single(item => item.Id == global.Id).UsageCount.ShouldBe(1);

        (await admin.Http.DeleteAsync($"/api/v1/global-ticket-template/{global.Id}", Token)).EnsureSuccessStatusCode();
        (await ListAsync(owner, project.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Space_templates_keep_their_mode()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var task = await TypeIdAsync(owner, project.Id, "Task");

        var hint = await CreateTemplateAsync(owner, project.Id, new { ticketTypeId = task, name = "Hint", content = "<p>Done when</p>", mode = 1, isDefault = true });
        hint.Mode.ShouldBe(TicketTemplateMode.Placeholder);
        hint.GlobalTemplateId.ShouldBeNull();

        var invalid = await CreateAsync(owner, project.Id, new { ticketTypeId = task, name = "Odd", content = "", mode = 7 });
        (await invalid.Content.ReadAsStringAsync(Token)).ShouldContain(TicketTemplateErrors.InvalidMode);
    }

    [Fact]
    public async Task Subtasks_and_other_spaces_types_are_refused()
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var project = await CreateProjectAsync(owner);
        var other = await CreateProjectAsync(owner);

        var subtask = await CreateAsync(owner, project.Id, new { ticketTypeId = await TypeIdAsync(owner, project.Id, "Subtask"), name = "Sub", content = "", isDefault = false });
        (await subtask.Content.ReadAsStringAsync(Token)).ShouldContain(TicketTemplateErrors.SubtaskType);

        var foreign = await CreateAsync(owner, project.Id, new { ticketTypeId = await TypeIdAsync(owner, other.Id, "Task"), name = "Foreign", content = "", isDefault = false });
        (await foreign.Content.ReadAsStringAsync(Token)).ShouldContain(TicketErrors.ProjectMismatch);

        var blank = await CreateAsync(owner, project.Id, new { ticketTypeId = await TypeIdAsync(owner, project.Id, "Task"), name = "  ", content = "", isDefault = false });
        blank.IsSuccessStatusCode.ShouldBeFalse();
    }
}
