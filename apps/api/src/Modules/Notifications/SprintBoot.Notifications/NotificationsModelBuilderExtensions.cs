using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SprintBoot.Notifications;

public static class NotificationsModelBuilderExtensions
{
    public const string Schema = "notifications";

    public static void ConfigureNotifications(this ModelBuilder builder)
    {
        builder.Entity<NotificationScheme>(b =>
        {
            b.ToTable("schemes", Schema);
            b.ConfigureByConvention();
            b.Property(scheme => scheme.Name).HasMaxLength(128).IsRequired();
            b.Property(scheme => scheme.Description).HasMaxLength(512);
        });
        builder.Entity<NotificationRule>(b =>
        {
            b.ToTable("rules", Schema);
            b.ConfigureByConvention();
            b.Property(rule => rule.EventType).HasMaxLength(64).IsRequired();
            b.Property(rule => rule.RecipientValue).HasMaxLength(64);
            b.HasIndex(rule => rule.SchemeId);
        });
        builder.Entity<ProjectNotificationScheme>(b =>
        {
            b.ToTable("project_schemes", Schema);
            b.ConfigureByConvention();
            b.HasIndex(item => item.ProjectId).IsUnique();
        });
        builder.Entity<Notification>(b =>
        {
            b.ToTable("notifications", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.EventType).HasMaxLength(64).IsRequired();
            b.Property(item => item.IssueKey).HasMaxLength(32);
            b.Property(item => item.Title).HasMaxLength(255);
            b.Property(item => item.ActorName).HasMaxLength(128);
            b.Property(item => item.Detail).HasMaxLength(512);
            b.HasIndex(item => new { item.UserId, item.EventId }).IsUnique();
            b.HasIndex(item => new { item.UserId, item.CreatedAt });
            b.HasIndex(item => item.CreatedAt);
        });
        builder.Entity<NotificationPreference>(b =>
        {
            b.ToTable("preferences", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.EventType).HasMaxLength(64).IsRequired();
            b.HasIndex(item => new { item.UserId, item.EventType }).IsUnique();
        });
    }
}
