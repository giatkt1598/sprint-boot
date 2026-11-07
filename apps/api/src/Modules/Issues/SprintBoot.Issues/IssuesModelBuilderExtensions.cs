using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SprintBoot.Issues;

public static class IssuesModelBuilderExtensions
{
    public const string Schema = "issues";

    public static void ConfigureIssues(this ModelBuilder builder)
    {
        builder.Entity<IssueType>(b =>
        {
            b.ToTable("issue_types", Schema);
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

        builder.Entity<Issue>(b =>
        {
            b.ToTable("issues", Schema);
            b.ConfigureByConvention();
            b.Property(issue => issue.Title).HasMaxLength(255).IsRequired();
            b.Property(issue => issue.Rank).HasMaxLength(128).UseCollation("C");
            b.HasIndex(issue => new { issue.ProjectId, issue.Rank });
            b.Property(issue => issue.Resolution).HasMaxLength(64);
            b.Property(issue => issue.StoryPoints).HasPrecision(6, 2);
            b.HasIndex(issue => new { issue.ProjectId, issue.Number }).IsUnique();
            b.HasIndex(issue => issue.ParentId);
            b.HasIndex(issue => new { issue.ProjectId, issue.LastModificationTime });
            b.HasGeneratedTsVectorColumn(issue => issue.SearchVector, "simple", issue => new { issue.Title, issue.DescriptionText })
                .HasIndex(issue => issue.SearchVector)
                .HasMethod("GIN");
        });

        builder.Entity<IssueLinkType>(b =>
        {
            b.ToTable("issue_link_types", Schema);
            b.ConfigureByConvention();
            b.Property(type => type.Name).HasMaxLength(IssueLinkConsts.MaxNameLength).IsRequired();
            b.Property(type => type.Outward).HasMaxLength(IssueLinkConsts.MaxNameLength).IsRequired();
            b.Property(type => type.Inward).HasMaxLength(IssueLinkConsts.MaxNameLength).IsRequired();
        });

        builder.Entity<IssueLink>(b =>
        {
            b.ToTable("issue_links", Schema);
            b.ConfigureByConvention();
            b.HasIndex(link => new { link.SourceIssueId, link.TargetIssueId, link.LinkTypeId }).IsUnique();
            b.HasIndex(link => link.TargetIssueId);
            b.HasIndex(link => link.LinkTypeId);
        });

        builder.Entity<IssueTemplate>(b =>
        {
            b.ToTable("issue_templates", Schema);
            b.ConfigureByConvention();
            b.Property(template => template.Name).HasMaxLength(IssueTemplateConsts.MaxNameLength).IsRequired();
            b.Property(template => template.Content).IsRequired();
            b.HasIndex(template => new { template.ProjectId, template.IssueTypeId });
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

        builder.Entity<GlobalIssueTemplate>(b =>
        {
            b.ToTable("global_issue_templates", Schema);
            b.ConfigureByConvention();
            b.Property(template => template.IssueTypeKey).HasMaxLength(IssueTemplateConsts.MaxTypeKeyLength).IsRequired();
            b.Property(template => template.Name).HasMaxLength(IssueTemplateConsts.MaxNameLength).IsRequired();
            b.Property(template => template.Content).IsRequired();
        });

        builder.Entity<ProjectIntroduction>(b =>
        {
            b.ToTable("project_introductions", Schema);
            b.ConfigureByConvention();
            b.Property(introduction => introduction.Content).IsRequired();
            b.HasIndex(introduction => introduction.ProjectId).IsUnique();
        });

        builder.Entity<RecentIssue>(b =>
        {
            b.ToTable("recent_issues", Schema);
            b.ConfigureByConvention();
            b.HasIndex(recent => new { recent.UserId, recent.IssueId }).IsUnique();
            b.HasIndex(recent => new { recent.UserId, recent.ViewedAt });
        });

        builder.Entity<Label>(b =>
        {
            b.ToTable("labels", Schema);
            b.ConfigureByConvention();
            b.Property(label => label.Name).HasMaxLength(64).IsRequired();
            b.HasIndex(label => new { label.ProjectId, label.Name }).IsUnique();
        });

        builder.Entity<IssueLabel>(b =>
        {
            b.ToTable("issue_labels", Schema);
            b.ConfigureByConvention();
            b.HasIndex(link => new { link.IssueId, link.LabelId }).IsUnique();
            b.HasIndex(link => link.LabelId);
        });

        builder.Entity<IssueWatcher>(b =>
        {
            b.ToTable("issue_watchers", Schema);
            b.ConfigureByConvention();
            b.HasIndex(watcher => new { watcher.IssueId, watcher.UserId }).IsUnique();
        });

        builder.Entity<Comment>(b =>
        {
            b.ToTable("comments", Schema);
            b.ConfigureByConvention();
            b.Property(comment => comment.Body).IsRequired();
            b.HasIndex(comment => comment.IssueId);
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
            b.HasIndex(attachment => attachment.IssueId);
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

        builder.Entity<IssueActivity>(b =>
        {
            b.ToTable("issue_activities", Schema);
            b.ConfigureByConvention();
            b.Property(activity => activity.Field).HasMaxLength(64).IsRequired();
            b.HasIndex(activity => new { activity.IssueId, activity.CreatedAt });
        });

        builder.Entity<WorkLog>(b =>
        {
            b.ToTable("work_logs", Schema);
            b.ConfigureByConvention();
            b.Property(log => log.Note).HasMaxLength(1000);
            b.HasIndex(log => log.IssueId);
        });
    }
}
