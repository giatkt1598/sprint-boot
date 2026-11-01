using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shouldly;
using SprintBoot.Issues;
using SprintBoot.Projects;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>A file exists exactly as long as the text that references it.</summary>
[Collection(ApiCollection.Name)]
public class RichTextFileTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(AuthClient Client, ProjectDto Project, Guid TypeId)> SpaceAsync()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var key = $"F{Random.Shared.Next(100000, 999999)}";
        var project = (await (await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Files", key, type = 0 }, Token)).Content.ReadFromJsonAsync<ProjectDto>(Token))!;
        var types = await client.Http.GetFromJsonAsync<List<IssueTypeDto>>($"/api/v1/issue/types/{project.Id}", Token);
        return (client, project, types!.Single(type => type.Name == "Task").Id);
    }

    private static async Task<RichTextFileDto> UploadAsync(AuthClient client, Guid projectId, string contentType = "image/png", string name = "a.png")
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", name);
        var response = await client.Http.PostAsync($"/api/v1/rich-text-file/upload/{projectId}", form, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<RichTextFileDto>(Token))!;
    }

    private Task<HttpResponseMessage> FetchAsync(string url) => fixture.Factory.CreateClient().GetAsync(url, Token);

    [Fact]
    public async Task A_description_keeps_its_file_until_the_text_drops_it()
    {
        var (client, project, typeId) = await SpaceAsync();
        var file = await UploadAsync(client, project.Id);
        var created = await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = project.Id, typeId, title = "Pic", description = $"<p>x</p><img src=\"{file.Url}\">" }, Token);
        var issue = (await created.Content.ReadFromJsonAsync<IssueDto>(Token))!;

        issue.Description.ShouldContain("/content?t=");
        (await FetchAsync(file.Url)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var update = await client.Http.PutAsJsonAsync($"/api/v1/issue/{issue.Id}", new { concurrencyStamp = issue.ConcurrencyStamp, fields = new { description = "<p>gone</p>" } }, Token);
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync(Token));

        (await FetchAsync(file.Url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stored_text_never_keeps_a_signature_and_saving_it_back_changes_nothing()
    {
        var (client, project, typeId) = await SpaceAsync();
        var file = await UploadAsync(client, project.Id);
        var created = await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = project.Id, typeId, title = "Pic", description = $"<img src=\"{file.Url}\">" }, Token);
        var issue = (await created.Content.ReadFromJsonAsync<IssueDto>(Token))!;

        var saved = await client.Http.PutAsJsonAsync($"/api/v1/issue/{issue.Id}", new { concurrencyStamp = issue.ConcurrencyStamp, fields = new { description = issue.Description } }, Token);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FetchAsync(file.Url)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_a_comment_removes_its_files_and_replies_files()
    {
        var (client, project, typeId) = await SpaceAsync();
        var issue = (await (await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = project.Id, typeId, title = "C" }, Token)).Content.ReadFromJsonAsync<IssueDto>(Token))!;
        var first = await UploadAsync(client, project.Id);
        var second = await UploadAsync(client, project.Id);
        var comment = (await (await client.Http.PostAsJsonAsync("/api/v1/comment", new { issueId = issue.Id, body = $"<img src=\"{first.Url}\">" }, Token)).Content.ReadFromJsonAsync<CommentDto>(Token))!;
        (await client.Http.PostAsJsonAsync("/api/v1/comment", new { issueId = issue.Id, parentId = comment.Id, body = $"<img src=\"{second.Url}\">" }, Token)).EnsureSuccessStatusCode();

        (await client.Http.DeleteAsync($"/api/v1/comment/{comment.Id}", Token)).EnsureSuccessStatusCode();

        (await FetchAsync(first.Url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await FetchAsync(second.Url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Abandoned_uploads_are_cleaned_up_and_used_ones_stay()
    {
        var (client, project, typeId) = await SpaceAsync();
        var abandoned = await UploadAsync(client, project.Id);
        var used = await UploadAsync(client, project.Id);
        (await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = project.Id, typeId, title = "U", description = $"<img src=\"{used.Url}\">" }, Token)).EnsureSuccessStatusCode();

        await fixture.CleanRichTextFilesAsync(DateTime.Now.AddMinutes(1));

        (await FetchAsync(abandoned.Url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await FetchAsync(used.Url)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Addresses_need_a_signature_and_html_never_opens_inline()
    {
        var (client, project, _) = await SpaceAsync();
        var page = await UploadAsync(client, project.Id, "text/html", "x.html");

        var signed = await FetchAsync(page.Url);
        signed.StatusCode.ShouldBe(HttpStatusCode.OK);
        signed.Content.Headers.ContentType!.MediaType.ShouldBe("application/octet-stream");
        (await FetchAsync(page.Url.Split('?')[0])).StatusCode.ShouldNotBe(HttpStatusCode.OK);

        var outsider = await fixture.CreateUserClientAsync(Token);
        using var form = new MultipartFormDataContent { { new ByteArrayContent([1]), "file", "a.png" } };
        (await outsider.Http.PostAsync($"/api/v1/rich-text-file/upload/{project.Id}", form, Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
