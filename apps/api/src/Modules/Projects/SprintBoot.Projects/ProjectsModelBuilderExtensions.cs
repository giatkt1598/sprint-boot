using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SprintBoot.Projects;

public static class ProjectsModelBuilderExtensions
{
    public const string Schema = "projects";

    public static void ConfigureProjects(this ModelBuilder builder)
    {
        builder.Entity<Project>(b =>
        {
            b.ToTable("projects", Schema);
            b.ConfigureByConvention();
            b.Property(project => project.Key).HasMaxLength(10).IsRequired();
            b.Property(project => project.Name).HasMaxLength(128).IsRequired();
            b.Property(project => project.BranchNameTemplate).HasMaxLength(128);
            b.Property(project => project.SprintLengthDays).HasDefaultValue(14);
            b.HasIndex(project => project.Key).IsUnique();
        });

        builder.Entity<ProjectMember>(b =>
        {
            b.ToTable("project_members", Schema);
            b.ConfigureByConvention();
            b.Property(member => member.Role).HasMaxLength(64).IsRequired();
            b.HasIndex(member => new { member.ProjectId, member.UserId }).IsUnique();
            b.HasIndex(member => member.UserId);
        });

        builder.Entity<ProjectRole>(b =>
        {
            b.ToTable("project_roles", Schema);
            b.ConfigureByConvention();
            b.Property(role => role.Name).HasMaxLength(64).IsRequired();
            b.HasIndex(role => new { role.ProjectId, role.Name }).IsUnique();
        });

        builder.Entity<ProjectRolePermission>(b =>
        {
            b.ToTable("project_role_permissions", Schema);
            b.ConfigureByConvention();
            b.Property(permission => permission.PermissionKey).HasMaxLength(64).IsRequired();
            b.HasIndex(permission => new { permission.RoleId, permission.PermissionKey }).IsUnique();
        });
    }
}
