using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SprintBoot.Agile;
using SprintBoot.Identity;
using SprintBoot.Notifications;
using SprintBoot.Projects;
using SprintBoot.Tickets;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>Acceptance criteria of SB-144: archiving makes a space read-only, deleting goes through the trash to a purge.</summary>
[Collection(ApiCollection.Name)]
public class SpaceArchiveTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Space(AuthClient Owner, ProjectDto Project, TicketDto Ticket, Guid TypeId);

    private static string NewKey() => $"Z{Random.Shared.Next(100000, 999999)}";

    private async Task<Space> SpaceAsync(string? key = null)
    {
        var owner = await fixture.CreateUserClientAsync(Token);
        var created = await owner.Http.PostAsJsonAsync("/api/v1/project", new { name = "Archive", key = key ?? NewKey(), type = 0 }, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Token));
        var project = (await created.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
        var types = await owner.Http.GetFromJsonAsync<List<TicketTypeDto>>($"/api/v1/ticket/types/{project.Id}", Token);
        var typeId = types!.Single(type => type.Name == "Task").Id;
        var ticket = await owner.Http.PostAsJsonAsync("/api/v1/ticket", new { projectId = project.Id, typeId, title = "Kept" }, Token);
        ticket.StatusCode.ShouldBe(HttpStatusCode.OK, await ticket.Content.ReadAsStringAsync(Token));
        return new Space(owner, project, (await ticket.Content.ReadFromJsonAsync<TicketDto>(Token))!, typeId);
    }

    private async Task<AuthClient> AdminAsync()
    {
        var admin = new AuthClient(fixture);
        await admin.SignInAsync(ApiFixture.AdminEmail, ApiFixture.AdminPassword, Token);
        return admin;
    }

    private static async Task<ProjectDto> ArchiveAsync(AuthClient client, ProjectDto project)
    {
        var response = await client.Http.PostAsync($"/api/v1/project/{project.Id}/archive", null, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<ProjectDto>(Token))!;
    }

    private static Task<HttpResponseMessage> DeleteAsync(AuthClient client, ProjectDto project, string confirmKey) =>
        client.Http.DeleteAsync($"/api/v1/project/{project.Id}?confirmKey={confirmKey}", Token);

    private static async Task ShouldBeArchivedRefusalAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldContain(ProjectErrors.Archived);
    }

    [Fact]
    public async Task An_archived_space_refuses_every_write_but_stays_readable()
    {
        var space = await SpaceAsync();
        var (owner, project, ticket) = (space.Owner, space.Project, space.Ticket);
        var comment = await (await owner.Http.PostAsJsonAsync("/api/v1/comment", new { ticketId = ticket.Id, body = "<p>before</p>" }, Token))
            .Content.ReadFromJsonAsync<CommentDto>(Token);

        var archived = await ArchiveAsync(owner, project);

        archived.ArchivedAt.ShouldNotBeNull();
        archived.Permissions.ShouldBe([ProjectPermissions.ViewTickets]);
        archived.CanArchive.ShouldBeTrue();

        await ShouldBeArchivedRefusalAsync(await owner.Http.PostAsJsonAsync("/api/v1/ticket", new { projectId = project.Id, typeId = space.TypeId, title = "New" }, Token));
        await ShouldBeArchivedRefusalAsync(await owner.Http.PutAsJsonAsync(
            $"/api/v1/ticket/{ticket.Id}", new { concurrencyStamp = ticket.ConcurrencyStamp, fields = new { title = "Changed" } }, Token));
        await ShouldBeArchivedRefusalAsync(await owner.Http.PostAsJsonAsync("/api/v1/comment", new { ticketId = ticket.Id, body = "<p>after</p>" }, Token));
        await ShouldBeArchivedRefusalAsync(await owner.Http.PutAsJsonAsync($"/api/v1/comment/{comment!.Id}", new { body = "<p>edited</p>" }, Token));
        await ShouldBeArchivedRefusalAsync(await owner.Http.DeleteAsync($"/api/v1/comment/{comment.Id}", Token));
        await ShouldBeArchivedRefusalAsync(await owner.Http.DeleteAsync($"/api/v1/ticket/{ticket.Id}", Token));
        await ShouldBeArchivedRefusalAsync(await owner.Http.PostAsJsonAsync("/api/v1/sprint", new { projectId = project.Id, name = "S1" }, Token));
        await ShouldBeArchivedRefusalAsync(await owner.Http.PutAsJsonAsync(
            $"/api/v1/project/{project.Id}", new { name = "Renamed", leadId = project.Lead?.Id }, Token));
        await ShouldBeArchivedRefusalAsync(await owner.Http.PostAsync($"/api/v1/watcher/watch/{ticket.Id}", null, Token));

        // Reading, by link and in search, still works.
        (await owner.Http.GetFromJsonAsync<TicketDto>($"/api/v1/ticket/by-key?key={ticket.Key}", Token))!.Title.ShouldBe("Kept");
        (await owner.Http.GetFromJsonAsync<ProjectDto>($"/api/v1/project/by-key?key={project.Key}", Token))!.ArchivedAt.ShouldNotBeNull();
        (await owner.Http.GetFromJsonAsync<TicketSearchResultDto>($"/api/v1/ticket/search?ProjectId={project.Id}", Token))!.TotalCount.ShouldBe(1);
        (await owner.Http.GetFromJsonAsync<TicketSearchResultDto>($"/api/v1/ticket/search?Text={ticket.Key}&ExcludeArchived=true", Token))!.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Archived_spaces_leave_the_default_list_and_restoring_brings_them_back_as_they_were()
    {
        var (owner, project, ticket, typeId) = await SpaceAsync();

        await ArchiveAsync(owner, project);

        (await owner.Http.GetFromJsonAsync<List<ProjectDto>>("/api/v1/project", Token))!.ShouldNotContain(item => item.Id == project.Id);
        (await owner.Http.GetFromJsonAsync<List<ProjectDto>>("/api/v1/project?includeArchived=true", Token))!.ShouldContain(item => item.Id == project.Id);

        var restore = await owner.Http.PostAsync($"/api/v1/project/{project.Id}/restore", null, Token);
        restore.StatusCode.ShouldBe(HttpStatusCode.OK, await restore.Content.ReadAsStringAsync(Token));
        var restored = (await restore.Content.ReadFromJsonAsync<ProjectDto>(Token))!;

        restored.ArchivedAt.ShouldBeNull();
        restored.Permissions.ShouldBe(project.Permissions, ignoreOrder: true);
        restored.Name.ShouldBe(project.Name);
        (await owner.Http.GetFromJsonAsync<TicketDto>($"/api/v1/ticket/by-key?key={ticket.Key}", Token))!.ConcurrencyStamp.ShouldBe(ticket.ConcurrencyStamp);
        (await owner.Http.PostAsJsonAsync("/api/v1/ticket", new { projectId = project.Id, typeId, title = "Again" }, Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_those_who_manage_a_space_can_archive_it()
    {
        var (owner, project, _, _) = await SpaceAsync();
        var member = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{project.Id}/member", new { userId = member.UserId, role = ProjectRoleNames.Member }, Token))
            .EnsureSuccessStatusCode();

        (await member.Http.PostAsync($"/api/v1/project/{project.Id}/archive", null, Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await ArchiveAsync(owner, project);
        (await member.Http.PostAsync($"/api/v1/project/{project.Id}/restore", null, Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Mcp_hides_archived_spaces_and_its_write_tools_explain_the_refusal()
    {
        var (owner, project, _, _) = await SpaceAsync();
        var token = await owner.Http.PostAsJsonAsync("/api/v1/personal-token", new { name = "Agent", kind = PersonalTokenKind.Mcp, readOnly = false, expiresInDays = 30 }, Token);
        token.EnsureSuccessStatusCode();
        var http = fixture.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await token.Content.ReadFromJsonAsync<CreatedPersonalTokenDto>(Token))!.Token);
        await ArchiveAsync(owner, project);

        var (listed, _) = await CallAsync(http, "list_spaces", new { });
        listed.ShouldNotContain($"\"{project.Key}\"");
        var (all, _) = await CallAsync(http, "list_spaces", new { includeArchived = true });
        all.ShouldContain($"\"{project.Key}\"");

        var (text, isError) = await CallAsync(http, "create_ticket", new { space = project.Key, title = "Refused" });
        isError.ShouldBeTrue();
        text.ShouldContain("archived");
        text.ShouldContain(project.Key);
    }

    [Fact]
    public async Task An_archived_space_sends_no_notifications()
    {
        var (owner, project, ticket, _) = await SpaceAsync();
        var member = await fixture.CreateUserClientAsync(Token);
        (await owner.Http.PostAsJsonAsync($"/api/v1/project/{project.Id}/member", new { userId = member.UserId, role = ProjectRoleNames.Member }, Token))
            .EnsureSuccessStatusCode();
        await ArchiveAsync(owner, project);

        await fixture.RunNotificationAsync(new NotificationEvent(
            Guid.NewGuid(), NotificationEventTypes.CommentAdded, project.Id, ticket.Id, owner.UserId, [member.UserId]));

        var inbox = await member.Http.GetFromJsonAsync<Volo.Abp.Application.Dtos.PagedResultDto<NotificationDto>>("/api/v1/notification?maxResultCount=50", Token);
        inbox!.Items.ShouldNotContain(item => item.TicketKey == ticket.Key && item.EventType == NotificationEventTypes.CommentAdded);
    }

    [Fact]
    public async Task Delete_needs_an_archived_space_the_admin_permission_and_the_typed_key()
    {
        var (owner, project, _, _) = await SpaceAsync();
        var admin = await AdminAsync();

        var active = await DeleteAsync(admin, project, project.Key);
        active.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await active.Content.ReadAsStringAsync(Token)).ShouldContain(ProjectErrors.NotArchived);

        await ArchiveAsync(owner, project);

        // Managing the space is not enough: deleting is the global permission.
        (await DeleteAsync(owner, project, project.Key)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var mistyped = await DeleteAsync(admin, project, "WRONG");
        mistyped.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await mistyped.Content.ReadAsStringAsync(Token)).ShouldContain(ProjectErrors.ConfirmKeyMismatch);

        (await DeleteAsync(admin, project, project.Key)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.Http.GetAsync($"/api/v1/project/by-key?key={project.Key}", Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await owner.Http.GetAsync($"/api/v1/ticket/search?Text={project.Key}", Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_deleted_space_waits_in_the_trash_holds_its_key_and_can_be_restored()
    {
        var (owner, project, ticket, _) = await SpaceAsync();
        var admin = await AdminAsync();
        await ArchiveAsync(owner, project);
        (await DeleteAsync(admin, project, project.Key)).EnsureSuccessStatusCode();

        var trash = (await admin.Http.GetFromJsonAsync<List<TrashedProjectDto>>("/api/v1/project/trash", Token))!;
        var entry = trash.Single(item => item.Id == project.Id);
        (entry.PurgeAt - entry.DeletedAt).ShouldBe(TimeSpan.FromDays(ProjectSettings.DefaultTrashRetentionDays));
        (await owner.Http.GetAsync("/api/v1/project/trash", Token)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var taken = await owner.Http.PostAsJsonAsync("/api/v1/project", new { name = "Again", key = project.Key, type = 0 }, Token);
        (await taken.Content.ReadAsStringAsync(Token)).ShouldContain(ProjectErrors.KeyTaken);

        var restore = await admin.Http.PostAsync($"/api/v1/project/{project.Id}/restore-deleted", null, Token);
        restore.StatusCode.ShouldBe(HttpStatusCode.OK, await restore.Content.ReadAsStringAsync(Token));
        (await restore.Content.ReadFromJsonAsync<ProjectDto>(Token))!.ArchivedAt.ShouldNotBeNull();
        (await owner.Http.GetFromJsonAsync<TicketDto>($"/api/v1/ticket/by-key?key={ticket.Key}", Token))!.Title.ShouldBe("Kept");
    }

    [Fact]
    public async Task The_purge_removes_the_space_from_every_module_and_frees_its_key()
    {
        var (owner, project, ticket, _) = await SpaceAsync();
        (await owner.Http.PostAsJsonAsync("/api/v1/comment", new { ticketId = ticket.Id, body = "<p>gone soon</p>" }, Token)).EnsureSuccessStatusCode();
        (await owner.Http.PostAsJsonAsync("/api/v1/sprint", new { projectId = project.Id, name = "S1" }, Token)).EnsureSuccessStatusCode();
        var bytes = new ByteArrayContent(new byte[64]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using (var file = new MultipartFormDataContent { { bytes, "file", "report.pdf" } })
        {
            var uploaded = await owner.Http.PostAsync($"/api/v1/attachment/upload/{ticket.Id}", file, Token);
            uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync(Token));
        }

        var storageKey = await RowsAsync<Attachment, string?>(query => query.Where(item => item.TicketId == ticket.Id).Select(item => item.StorageKey).FirstOrDefault());
        storageKey.ShouldNotBeNull();
        var admin = await AdminAsync();
        await ArchiveAsync(owner, project);
        (await DeleteAsync(admin, project, project.Key)).EnsureSuccessStatusCode();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectPurger>().PurgeAsync(project.Id);
        }

        await WaitUntilAsync(async () =>
            await RowsAsync<Ticket, int>(query => query.Count(item => item.ProjectId == project.Id)) == 0
            && await RowsAsync<Comment, int>(query => query.Count(item => item.TicketId == ticket.Id)) == 0
            && await RowsAsync<Attachment, int>(query => query.Count(item => item.TicketId == ticket.Id)) == 0
            && await RowsAsync<Board, int>(query => query.Count(item => item.ProjectId == project.Id)) == 0
            && await RowsAsync<SprintBoot.Workflows.ProjectWorkflowMapping, int>(query => query.Count(item => item.ProjectId == project.Id)) == 0);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var container = scope.ServiceProvider.GetRequiredService<Volo.Abp.BlobStoring.IBlobContainer<AttachmentContainer>>();
            (await container.ExistsAsync(storageKey, Token)).ShouldBeFalse();
        }

        (await admin.Http.GetFromJsonAsync<List<TrashedProjectDto>>("/api/v1/project/trash", Token))!.ShouldNotContain(item => item.Id == project.Id);
        var again = await owner.Http.PostAsJsonAsync("/api/v1/project", new { name = "Again", key = project.Key, type = 0 }, Token);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await again.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task The_purge_job_leaves_spaces_whose_grace_period_has_not_run_out()
    {
        var (owner, project, _, _) = await SpaceAsync();
        var admin = await AdminAsync();
        await ArchiveAsync(owner, project);
        (await DeleteAsync(admin, project, project.Key)).EnsureSuccessStatusCode();

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProjectPurger>().PurgeDueAsync();
        }

        (await admin.Http.GetFromJsonAsync<List<TrashedProjectDto>>("/api/v1/project/trash", Token))!.ShouldContain(item => item.Id == project.Id);
    }

    private async Task<TResult> RowsAsync<TEntity, TResult>(Func<IQueryable<TEntity>, TResult> read)
        where TEntity : class, Volo.Abp.Domain.Entities.IEntity<Guid>
    {
        using var scope = fixture.Factory.Services.CreateScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        using var _ = scope.ServiceProvider.GetRequiredService<IDataFilter>().Disable<ISoftDelete>();
        var query = await scope.ServiceProvider.GetRequiredService<IRepository<TEntity, Guid>>().GetQueryableAsync();
        return read(query);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> done)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (await done())
            {
                return;
            }

            await Task.Delay(200, Token);
        }

        throw new TimeoutException("The purge did not reach every module.");
    }

    private static async Task<(string Text, bool IsError)> CallAsync(HttpClient http, string tool, object arguments)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments } }),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        var response = await http.SendAsync(request, Token);
        var body = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        var json = body.Split('\n').FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal))?[5..] ?? body;
        var result = JsonDocument.Parse(json).RootElement.GetProperty("result");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        return (text, result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
    }
}
