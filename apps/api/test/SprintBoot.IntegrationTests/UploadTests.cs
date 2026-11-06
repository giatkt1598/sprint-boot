using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SprintBoot.Issues;
using SprintBoot.Projects;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace SprintBoot.IntegrationTests;

/// <summary>Files go up in parts, so no request is larger than a proxy in front of the API accepts.</summary>
[Collection(ApiCollection.Name)]
public class UploadTests(ApiFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly long PartSize = ApiFixture.UploadPartSizeKb * 1024L;

    private async Task<(AuthClient Client, ProjectDto Project, IssueDto Issue)> IssueAsync()
    {
        var client = await fixture.CreateUserClientAsync(Token);
        var key = $"U{Random.Shared.Next(100000, 999999)}";
        var project = (await (await client.Http.PostAsJsonAsync("/api/v1/project", new { name = "Uploads", key, type = 0 }, Token)).Content.ReadFromJsonAsync<ProjectDto>(Token))!;
        var types = await client.Http.GetFromJsonAsync<List<IssueTypeDto>>($"/api/v1/issue/types/{project.Id}", Token);
        var created = await client.Http.PostAsJsonAsync("/api/v1/issue", new { projectId = project.Id, typeId = types!.Single(type => type.Name == "Task").Id, title = "Files" }, Token);
        return (client, project, (await created.Content.ReadFromJsonAsync<IssueDto>(Token))!);
    }

    private static byte[] Bytes(long size) => [.. Enumerable.Range(0, (int)size).Select(index => (byte)(index % 251))];

    private static async Task<HttpResponseMessage> StartAsync(AuthClient client, object body) =>
        await client.Http.PostAsJsonAsync("/api/v1/upload/start", body, Token);

    private static async Task<UploadSessionDto> StartOkAsync(AuthClient client, object body)
    {
        var response = await StartAsync(client, body);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<UploadSessionDto>(Token))!;
    }

    private static async Task<HttpResponseMessage> PartAsync(AuthClient client, Guid id, int number, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "content", "part" } };
        return await client.Http.PostAsync($"/api/v1/upload/{id}/upload-part?number={number}", form, Token);
    }

    private static async Task<List<UploadedPartDto>> SendAllAsync(AuthClient client, UploadSessionDto session, byte[] file)
    {
        var parts = new List<UploadedPartDto>();
        foreach (var target in session.Parts)
        {
            target.Url.ShouldBeNull();
            var start = (target.Number - 1) * (int)session.PartSize;
            var response = await PartAsync(client, session.Id, target.Number, file[start..Math.Min(file.Length, start + (int)session.PartSize)]);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
            parts.Add((await response.Content.ReadFromJsonAsync<UploadedPartDto>(Token))!);
        }

        return parts;
    }

    private static Task<HttpResponseMessage> CompleteAsync(AuthClient client, Guid id, List<UploadedPartDto> parts) =>
        client.Http.PostAsJsonAsync($"/api/v1/upload/{id}/complete", new { parts }, Token);

    [Fact]
    public async Task An_attachment_goes_up_in_parts_and_reads_back_whole()
    {
        var (client, _, issue) = await IssueAsync();
        var file = Bytes((PartSize * 2) + 123);

        var session = await StartOkAsync(client, new { purpose = UploadPurpose.Attachment, issueId = issue.Id, fileName = "build.zip", contentType = "application/zip", size = file.Length });
        session.Parts.Count.ShouldBe(3);
        session.PartSize.ShouldBe(PartSize);

        var parts = await SendAllAsync(client, session, file);
        var completed = await CompleteAsync(client, session.Id, parts);
        completed.StatusCode.ShouldBe(HttpStatusCode.OK, await completed.Content.ReadAsStringAsync(Token));
        var attachment = (await completed.Content.ReadFromJsonAsync<UploadResultDto>(Token))!.Attachment!;
        attachment.FileName.ShouldBe("build.zip");
        attachment.Size.ShouldBe(file.Length);

        (await client.Http.GetByteArrayAsync($"/api/v1/attachment/{attachment.Id}/download", Token)).ShouldBe(file);
        // A finished session is gone.
        (await CompleteAsync(client, session.Id, parts)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_rich_text_file_goes_up_in_parts()
    {
        var (client, project, _) = await IssueAsync();
        var file = Bytes(PartSize + 1);

        var session = await StartOkAsync(client, new { purpose = UploadPurpose.RichText, projectId = project.Id, fileName = "clip.mp4", contentType = "video/mp4", size = file.Length });
        var completed = await CompleteAsync(client, session.Id, await SendAllAsync(client, session, file));
        completed.StatusCode.ShouldBe(HttpStatusCode.OK, await completed.Content.ReadAsStringAsync(Token));
        var stored = (await completed.Content.ReadFromJsonAsync<UploadResultDto>(Token))!.RichTextFile!;

        stored.ContentType.ShouldBe("video/mp4");
        (await fixture.Factory.CreateClient().GetByteArrayAsync(stored.Url, Token)).ShouldBe(file);
    }

    [Fact]
    public async Task Wrong_sizes_missing_parts_and_oversized_files_are_refused()
    {
        var (client, _, issue) = await IssueAsync();
        var file = Bytes(PartSize + 10);

        var tooLarge = await StartAsync(client, new { purpose = UploadPurpose.Attachment, issueId = issue.Id, fileName = "big.bin", size = 2L * 1024 * 1024 });
        (await tooLarge.Content.ReadAsStringAsync(Token)).ShouldContain(IssueErrors.AttachmentTooLarge);

        var noTarget = await StartAsync(client, new { purpose = UploadPurpose.Attachment, fileName = "a.bin", size = 10 });
        (await noTarget.Content.ReadAsStringAsync(Token)).ShouldContain(UploadErrors.TargetMissing);

        var session = await StartOkAsync(client, new { purpose = UploadPurpose.Attachment, issueId = issue.Id, fileName = "a.bin", size = file.Length });
        // Every part but the last is full; the last holds exactly the rest.
        (await (await PartAsync(client, session.Id, 1, file[..10])).Content.ReadAsStringAsync(Token)).ShouldContain(UploadErrors.PartInvalid);
        (await (await PartAsync(client, session.Id, 3, file[..10])).Content.ReadAsStringAsync(Token)).ShouldContain(UploadErrors.PartInvalid);

        var first = (await (await PartAsync(client, session.Id, 1, file[..(int)PartSize])).Content.ReadFromJsonAsync<UploadedPartDto>(Token))!;
        (await (await CompleteAsync(client, session.Id, [first])).Content.ReadAsStringAsync(Token)).ShouldContain(UploadErrors.Incomplete);
        // Claiming a part that never arrived does not help.
        (await (await CompleteAsync(client, session.Id, [first, new UploadedPartDto { Number = 2, ETag = "x" }])).Content.ReadAsStringAsync(Token))
            .ShouldContain(UploadErrors.Incomplete);

        // A part sent again replaces the earlier try, so a failed part can simply be retried.
        await PartAsync(client, session.Id, 2, file[(int)PartSize..]);
        var second = (await (await PartAsync(client, session.Id, 2, file[(int)PartSize..])).Content.ReadFromJsonAsync<UploadedPartDto>(Token))!;
        (await CompleteAsync(client, session.Id, [first, second])).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_people_who_edit_the_space_upload_and_sessions_stay_private()
    {
        var (owner, project, issue) = await IssueAsync();
        var outsider = await fixture.CreateUserClientAsync(Token);
        (await StartAsync(outsider, new { purpose = UploadPurpose.Attachment, issueId = issue.Id, fileName = "a.bin", size = 10 })).IsSuccessStatusCode.ShouldBeFalse();
        (await StartAsync(outsider, new { purpose = UploadPurpose.RichText, projectId = project.Id, fileName = "a.bin", size = 10 })).IsSuccessStatusCode.ShouldBeFalse();

        var session = await StartOkAsync(owner, new { purpose = UploadPurpose.Attachment, issueId = issue.Id, fileName = "a.bin", size = 10 });
        (await PartAsync(outsider, session.Id, 1, Bytes(10))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.Http.PostAsync($"/api/v1/upload/{session.Id}/renew", null, Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.Http.PostAsync($"/api/v1/upload/{session.Id}/abort", null, Token)).EnsureSuccessStatusCode();
        (await PartAsync(owner, session.Id, 1, Bytes(10))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Expired_sessions_refuse_parts_and_are_cleaned_up()
    {
        var (client, _, issue) = await IssueAsync();
        var session = await StartOkAsync(client, new { purpose = UploadPurpose.Attachment, issueId = issue.Id, fileName = "a.bin", size = 10 });
        (await PartAsync(client, session.Id, 1, Bytes(10))).StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            await UploadCleanupWorker.CleanAsync(scope.ServiceProvider, DateTime.UtcNow.AddDays(2));
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var uow = unitOfWork.Begin();
            (await scope.ServiceProvider.GetRequiredService<IRepository<UploadSession, Guid>>().FindAsync(session.Id, cancellationToken: Token)).ShouldBeNull();
        }

        (await PartAsync(client, session.Id, 1, Bytes(10))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
