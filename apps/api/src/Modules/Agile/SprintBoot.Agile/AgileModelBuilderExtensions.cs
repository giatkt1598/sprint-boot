using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SprintBoot.Agile;

public static class AgileModelBuilderExtensions
{
    public const string Schema = "agile";

    public static void ConfigureAgile(this ModelBuilder builder)
    {
        builder.Entity<Board>(b =>
        {
            b.ToTable("boards", Schema);
            b.ConfigureByConvention();
            b.Property(board => board.Name).HasMaxLength(128).IsRequired();
            b.HasIndex(board => board.ProjectId).IsUnique();
        });
        builder.Entity<BoardColumn>(b =>
        {
            b.ToTable("board_columns", Schema);
            b.ConfigureByConvention();
            b.Property(column => column.Name).HasMaxLength(64).IsRequired();
            b.HasIndex(column => column.BoardId);
        });
        builder.Entity<BoardColumnStatus>(b =>
        {
            b.ToTable("board_column_statuses", Schema);
            b.ConfigureByConvention();
            b.HasIndex(item => new { item.ColumnId, item.StatusId }).IsUnique();
        });
        builder.Entity<BoardEntry>(b =>
        {
            b.ToTable("board_entries", Schema);
            b.ConfigureByConvention();
            b.HasIndex(entry => new { entry.BoardId, entry.IssueId }).IsUnique();
        });
        builder.Entity<Sprint>(b =>
        {
            b.ToTable("sprints", Schema);
            b.ConfigureByConvention();
            b.Property(sprint => sprint.Name).HasMaxLength(128).IsRequired();
            b.Property(sprint => sprint.Goal).HasMaxLength(512);
            b.HasIndex(sprint => sprint.BoardId);
            // The database is the final guard against two concurrent starts.
            b.HasIndex(sprint => sprint.BoardId).HasDatabaseName("IX_sprints_one_active")
                .IsUnique().HasFilter($"\"State\" = {(int)SprintState.Active}");
        });
        builder.Entity<SprintIssue>(b =>
        {
            b.ToTable("sprint_issues", Schema);
            b.ConfigureByConvention();
            b.HasIndex(item => item.SprintId);
            b.HasIndex(item => item.IssueId);
        });
    }
}
