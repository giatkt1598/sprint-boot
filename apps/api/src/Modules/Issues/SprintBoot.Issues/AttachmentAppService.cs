using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using SprintBoot.Identity;
using SprintBoot.Projects;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace SprintBoot.Issues;

[BlobContainerName("attachments")]
public class AttachmentContainer;

[Authorize]
public class AttachmentAppService(
    IRepository<Issue, Guid> issues,
    IRepository<Attachment, Guid> attachments,
    IRepository<IssueActivity, Guid> activities,
    IBlobContainer<AttachmentContainer> container,
    IProjectAccess projectAccess,
    ICurrentActor currentActor,
    IClock clock,
    IConfiguration configuration,
    AttachmentRecorder recorder)
    : ApplicationService, IAttachmentAppService
{
    public async Task<List<AttachmentDto>> GetListAsync(Guid issueId)
    {
        var issue = await issues.GetAsync(issueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);
        var list = await attachments.GetListAsync(item => item.IssueId == issueId);
        return await recorder.ToDtosAsync(list.OrderBy(item => item.CreationTime));
    }

    public async Task<AttachmentDto> UploadAsync(Guid issueId, IRemoteStreamContent file)
    {
        var issue = await issues.GetAsync(issueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.EditIssues);
        AttachmentRecorder.CheckSize(file.ContentLength, configuration);

        var id = GuidGenerator.Create();
        var storageKey = id.ToString("N");
        await using var stream = file.GetStream();
        await container.SaveAsync(storageKey, stream);

        return await recorder.RecordAsync(issueId, id, Path.GetFileName(file.FileName ?? "file"), file.ContentLength!.Value, file.ContentType, storageKey);
    }

    public async Task<IRemoteStreamContent> GetDownloadAsync(Guid id)
    {
        var attachment = await attachments.GetAsync(id);
        var issue = await issues.GetAsync(attachment.IssueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.ViewIssues);

        // Served as a download so uploaded HTML or SVG never runs on our origin.
        var stream = await container.GetAsync(attachment.StorageKey);
        return new RemoteStreamContent(stream, attachment.FileName, "application/octet-stream", attachment.Size);
    }

    public async Task DeleteAsync(Guid id)
    {
        var attachment = await attachments.GetAsync(id);
        var issue = await issues.GetAsync(attachment.IssueId);
        await projectAccess.CheckAsync(issue.ProjectId, ProjectPermissions.EditIssues);

        await attachments.DeleteAsync(attachment);
        await container.DeleteAsync(attachment.StorageKey);
        await activities.InsertAsync(new IssueActivity(
            GuidGenerator.Create(), issue.Id, currentActor.Actor, IssueFieldKeys.Attachment, attachment.FileName, null, clock.Now));
    }
}
