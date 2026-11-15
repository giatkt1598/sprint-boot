using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SprintBoot.Tickets;

public static class TicketsModelBuilderExtensions
{
    public const string Schema = "tickets";

    public static void ConfigureTickets(this ModelBuilder builder)
    {
        builder.Entity<TicketType>(b =>
        {
            b.ToTable("ticket_types", Schema);
            b.ConfigureByConvention();
            b.Property(type => type.Name).HasMaxLength(64).IsRequired();
            b.Property(type => type.Icon).HasMaxLength(32).IsRequired();
            b.HasIndex(type => new { type.ProjectId, type.Name }).IsUnique();
        });

        builder.Entity<Priority>(b =>
        {
            b.ToTable("priorities", Schema);
            b.ConfigureByConvention();
            b.Property(priority => priority.Name).HasMaxLength(64).IsRequired();
        });

        builder.Entity<Ticket>(b =>
        {
            b.ToTable("tickets", Schema);
            b.ConfigureByConvention();
            b.Property(ticket => ticket.Title).HasMaxLength(255).IsRequired();
            b.Property(ticket => ticket.Rank).HasMaxLength(128).UseCollation("C");
            b.HasIndex(ticket => new { ticket.ProjectId, ticket.Rank });
            b.Property(ticket => ticket.Resolution).HasMaxLength(64);
            b.Property(ticket => ticket.StoryPoints).HasPrecision(6, 2);
            b.HasIndex(ticket => new { ticket.ProjectId, ticket.Number }).IsUnique();
            b.HasIndex(ticket => ticket.ParentId);
            b.HasIndex(ticket => new { ticket.ProjectId, ticket.LastModificationTime });
            b.HasGeneratedTsVectorColumn(ticket => ticket.SearchVector, "simple", ticket => new { ticket.Title, ticket.DescriptionText })
                .HasIndex(ticket => ticket.SearchVector)
                .HasMethod("GIN");
        });

        builder.Entity<TicketLinkType>(b =>
        {
            b.ToTable("ticket_link_types", Schema);
            b.ConfigureByConvention();
            b.Property(type => type.Name).HasMaxLength(TicketLinkConsts.MaxNameLength).IsRequired();
            b.Property(type => type.Outward).HasMaxLength(TicketLinkConsts.MaxNameLength).IsRequired();
            b.Property(type => type.Inward).HasMaxLength(TicketLinkConsts.MaxNameLength).IsRequired();
        });

        builder.Entity<TicketLink>(b =>
        {
            b.ToTable("ticket_links", Schema);
            b.ConfigureByConvention();
            b.HasIndex(link => new { link.SourceTicketId, link.TargetTicketId, link.LinkTypeId }).IsUnique();
            b.HasIndex(link => link.TargetTicketId);
            b.HasIndex(link => link.LinkTypeId);
        });

        builder.Entity<TicketTemplate>(b =>
        {
            b.ToTable("ticket_templates", Schema);
            b.ConfigureByConvention();
            b.Property(template => template.Name).HasMaxLength(TicketTemplateConsts.MaxNameLength).IsRequired();
            b.Property(template => template.Content).IsRequired();
            b.HasIndex(template => new { template.ProjectId, template.TicketTypeId });
            b.HasIndex(template => template.GlobalTemplateId);
        });

        builder.Entity<UploadSession>(b =>
        {
            b.ToTable("upload_sessions", Schema);
            b.ConfigureByConvention();
            b.Property(session => session.FileName).HasMaxLength(UploadConsts.MaxFileNameLength).IsRequired();
            b.Property(session => session.ContentType).HasMaxLength(UploadConsts.MaxContentTypeLength).IsRequired();
            b.Property(session => session.StorageUploadId).HasMaxLength(1024);
            b.Ignore(session => session.PartCount);
            b.Ignore(session => session.StorageKey);
            b.HasIndex(session => session.ExpiresAt);
        });

        builder.Entity<GlobalTicketTemplate>(b =>
        {
            b.ToTable("global_ticket_templates", Schema);
            b.ConfigureByConvention();
            b.Property(template => template.TicketTypeKey).HasMaxLength(TicketTemplateConsts.MaxTypeKeyLength).IsRequired();
            b.Property(template => template.Name).HasMaxLength(TicketTemplateConsts.MaxNameLength).IsRequired();
            b.Property(template => template.Content).IsRequired();
        });

        builder.Entity<ProjectIntroduction>(b =>
        {
            b.ToTable("project_introductions", Schema);
            b.ConfigureByConvention();
            b.Property(introduction => introduction.Content).IsRequired();
            b.HasIndex(introduction => introduction.ProjectId).IsUnique();
        });

        builder.Entity<Goal>(b =>
        {
            b.ToTable("goals", Schema);
            b.ConfigureByConvention();
            b.Property(goal => goal.Name).HasMaxLength(GoalConsts.MaxNameLength).IsRequired();
            b.Property(goal => goal.Description).IsRequired();
            b.HasIndex(goal => new { goal.ProjectId, goal.ArchivedAt });
        });

        builder.Entity<GoalUpdate>(b =>
        {
            b.ToTable("goal_updates", Schema);
            b.ConfigureByConvention();
            b.Property(update => update.Note).HasMaxLength(GoalConsts.MaxNoteLength);
            b.HasIndex(update => new { update.GoalId, update.CreatedAt });
        });

        builder.Entity<RecentTicket>(b =>
        {
            b.ToTable("recent_tickets", Schema);
            b.ConfigureByConvention();
            b.HasIndex(recent => new { recent.UserId, recent.TicketId }).IsUnique();
            b.HasIndex(recent => new { recent.UserId, recent.ViewedAt });
        });

        builder.Entity<Label>(b =>
        {
            b.ToTable("labels", Schema);
            b.ConfigureByConvention();
            b.Property(label => label.Name).HasMaxLength(64).IsRequired();
            b.HasIndex(label => new { label.ProjectId, label.Name }).IsUnique();
        });

        builder.Entity<TicketLabel>(b =>
        {
            b.ToTable("ticket_labels", Schema);
            b.ConfigureByConvention();
            b.HasIndex(link => new { link.TicketId, link.LabelId }).IsUnique();
            b.HasIndex(link => link.LabelId);
        });

        builder.Entity<TicketWatcher>(b =>
        {
            b.ToTable("ticket_watchers", Schema);
            b.ConfigureByConvention();
            b.HasIndex(watcher => new { watcher.TicketId, watcher.UserId }).IsUnique();
        });

        builder.Entity<Comment>(b =>
        {
            b.ToTable("comments", Schema);
            b.ConfigureByConvention();
            b.Property(comment => comment.Body).IsRequired();
            b.HasIndex(comment => comment.TicketId);
        });

        builder.Entity<CommentReaction>(b =>
        {
            b.ToTable("comment_reactions", Schema);
            b.ConfigureByConvention();
            b.Property(reaction => reaction.Emoji).HasMaxLength(16).IsRequired();
            // Reactions from before the column count as given when it was added.
            b.Property(reaction => reaction.CreatedAt).HasDefaultValueSql("now()");
            b.HasIndex(reaction => new { reaction.CommentId, reaction.UserId, reaction.Emoji }).IsUnique();
        });

        builder.Entity<Attachment>(b =>
        {
            b.ToTable("attachments", Schema);
            b.ConfigureByConvention();
            b.Property(attachment => attachment.FileName).HasMaxLength(255).IsRequired();
            b.Property(attachment => attachment.ContentType).HasMaxLength(128).IsRequired();
            b.Property(attachment => attachment.StorageKey).HasMaxLength(128).IsRequired();
            b.HasIndex(attachment => attachment.TicketId);
        });

        builder.Entity<RichTextFile>(b =>
        {
            b.ToTable("rich_text_files", Schema);
            b.ConfigureByConvention();
            b.Property(file => file.FileName).HasMaxLength(255).IsRequired();
            b.Property(file => file.ContentType).HasMaxLength(128).IsRequired();
            b.Ignore(file => file.StorageKey);
            b.HasIndex(file => file.OwnerId);
        });

        builder.Entity<TicketActivity>(b =>
        {
            b.ToTable("ticket_activities", Schema);
            b.ConfigureByConvention();
            b.Property(activity => activity.Field).HasMaxLength(64).IsRequired();
            b.HasIndex(activity => new { activity.TicketId, activity.CreatedAt });
        });

        builder.Entity<WorkLog>(b =>
        {
            b.ToTable("work_logs", Schema);
            b.ConfigureByConvention();
            b.Property(log => log.Note).HasMaxLength(1000);
            b.HasIndex(log => log.TicketId);
        });

        builder.Entity<CustomField>(b =>
        {
            b.ToTable("custom_fields", Schema);
            b.ConfigureByConvention();
            b.Property(field => field.Key).HasMaxLength(CustomFieldConsts.MaxKeyLength).IsRequired();
            b.Property(field => field.Name).HasMaxLength(CustomFieldConsts.MaxNameLength).IsRequired();
            b.Property(field => field.Description).HasMaxLength(CustomFieldConsts.MaxDescriptionLength);
            b.Property(field => field.DefaultValue).HasMaxLength(CustomFieldConsts.MaxTextLength * 2);
            b.HasIndex(field => new { field.ProjectId, field.Key });
        });

        builder.Entity<CustomFieldOption>(b =>
        {
            b.ToTable("custom_field_options", Schema);
            b.ConfigureByConvention();
            b.Property(option => option.Label).HasMaxLength(CustomFieldConsts.MaxOptionLength).IsRequired();
            b.Property(option => option.Color).HasMaxLength(CustomFieldConsts.MaxColorLength);
            b.HasOne<CustomField>().WithMany().HasForeignKey(option => option.FieldId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProjectCustomField>(b =>
        {
            b.ToTable("project_custom_fields", Schema);
            b.ConfigureByConvention();
            b.HasIndex(row => new { row.ProjectId, row.FieldId }).IsUnique();
            b.HasOne<CustomField>().WithMany().HasForeignKey(row => row.FieldId).OnDelete(DeleteBehavior.Cascade);
        });

        // One column per kind, each indexed with the field, so a filter or a sort by a custom field reads an index.
        builder.Entity<TicketFieldValue>(b =>
        {
            b.ToTable("ticket_field_values", Schema);
            b.ConfigureByConvention();
            b.Property(value => value.TextValue).HasMaxLength(CustomFieldConsts.MaxTextLength);
            b.Property(value => value.NumberValue).HasPrecision(18, 4);
            b.HasIndex(value => new { value.TicketId, value.FieldId });
            b.HasIndex(value => new { value.FieldId, value.NumberValue });
            b.HasIndex(value => new { value.FieldId, value.DateValue });
            b.HasIndex(value => new { value.FieldId, value.OptionId });
            b.HasIndex(value => new { value.FieldId, value.UserId });
            b.HasOne<CustomField>().WithMany().HasForeignKey(value => value.FieldId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<CustomFieldOption>().WithMany().HasForeignKey(value => value.OptionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Ticket>().WithMany().HasForeignKey(value => value.TicketId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
