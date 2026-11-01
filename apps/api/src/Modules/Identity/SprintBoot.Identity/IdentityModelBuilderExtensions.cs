using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace SprintBoot.Identity;

public static class IdentityModelBuilderExtensions
{
    public const string Schema = "access";

    public static void ConfigureSprintIdentity(this ModelBuilder builder)
    {
        builder.Entity<ExternalProviderConfig>(b =>
        {
            b.ToTable("external_providers", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.Name).HasMaxLength(32).IsRequired();
            b.Property(item => item.DisplayName).HasMaxLength(64).IsRequired();
            b.Property(item => item.DiscoveryUrl).HasMaxLength(512);
            b.Property(item => item.ClientId).HasMaxLength(256).IsRequired();
            b.Property(item => item.Scopes).HasMaxLength(256);
            b.Property(item => item.EmailClaim).HasMaxLength(64);
            b.Property(item => item.NameClaim).HasMaxLength(64);
            b.HasIndex(item => item.Name).IsUnique();
        });
        builder.Entity<PersonalToken>(b =>
        {
            b.ToTable("personal_tokens", Schema);
            b.Property(token => token.Name).HasMaxLength(128).IsRequired();
            b.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
            b.Property(token => token.Prefix).HasMaxLength(16).IsRequired();
            b.HasIndex(token => token.TokenHash).IsUnique();
            b.HasIndex(token => token.UserId);
        });

        builder.Entity<AuthPolicy>(b =>
        {
            b.ToTable("auth_policy", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.AllowedDomains).HasMaxLength(512);
        });
        builder.Entity<Invitation>(b =>
        {
            b.ToTable("invitations", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.Email).HasMaxLength(256).IsRequired();
            b.Property(item => item.TokenHash).HasMaxLength(64).IsRequired();
            b.Property(item => item.SystemRole).HasMaxLength(64);
            b.HasIndex(item => item.TokenHash).IsUnique();
            b.HasIndex(item => item.Email);
        });
        builder.Entity<InvitationProject>(b =>
        {
            b.ToTable("invitation_projects", Schema);
            b.ConfigureByConvention();
            b.Property(item => item.ProjectRole).HasMaxLength(64).IsRequired();
            b.HasIndex(item => item.InvitationId);
        });
    }
}
