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

namespace SprintBoot.Tickets;

[BlobContainerName("attachments")]
public class AttachmentContainer;

[Authorize]
public class AttachmentAppService(
    IRepository<Ticket, Guid> tickets,
    IRepository<Attachment, Guid> attachments,
    IRepository<TicketActivity, Guid> activities,
    IBlobContainer<AttachmentContainer> container,
    IProjectAccess projectAccess,
    ICurrentActor currentActor,
    IClock clock,
    IConfiguration configuration,
    AttachmentRecorder recorder)
    : ApplicationService, IAttachmentAppService
{
    public async Task<List<AttachmentDto>> GetListAsync(Guid ticketId)
    {
        var ticket = await tickets.GetAsync(ticketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);
        var list = await attachments.GetListAsync(item => item.TicketId == ticketId);
        return await recorder.ToDtosAsync(list.OrderBy(item => item.CreationTime));
    }

    public async Task<AttachmentDto> UploadAsync(Guid ticketId, IRemoteStreamContent file)
    {
        var ticket = await tickets.GetAsync(ticketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.EditTickets);
        AttachmentRecorder.CheckSize(file.ContentLength, configuration);

        var id = GuidGenerator.Create();
        var storageKey = id.ToString("N");
        await using var stream = file.GetStream();
        await container.SaveAsync(storageKey, stream);

        return await recorder.RecordAsync(ticketId, id, Path.GetFileName(file.FileName ?? "file"), file.ContentLength!.Value, file.ContentType, storageKey);
    }

    public async Task<IRemoteStreamContent> GetDownloadAsync(Guid id)
    {
        var attachment = await attachments.GetAsync(id);
        var ticket = await tickets.GetAsync(attachment.TicketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.ViewTickets);

        // Served as a download so uploaded HTML or SVG never runs on our origin.
        var stream = await container.GetAsync(attachment.StorageKey);
        return new RemoteStreamContent(stream, attachment.FileName, "application/octet-stream", attachment.Size);
    }

    public async Task DeleteAsync(Guid id)
    {
        var attachment = await attachments.GetAsync(id);
        var ticket = await tickets.GetAsync(attachment.TicketId);
        await projectAccess.CheckAsync(ticket.ProjectId, ProjectPermissions.EditTickets);

        await attachments.DeleteAsync(attachment);
        await container.DeleteAsync(attachment.StorageKey);
        await activities.InsertAsync(new TicketActivity(
            GuidGenerator.Create(), ticket.Id, currentActor.Actor, TicketFieldKeys.Attachment, attachment.FileName, null, clock.Now));
    }
}
