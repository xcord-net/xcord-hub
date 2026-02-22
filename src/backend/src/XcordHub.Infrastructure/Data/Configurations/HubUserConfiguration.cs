using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class HubUserConfiguration : IEntityTypeConfiguration<HubUser>
{
    public void Configure(EntityTypeBuilder<HubUser> builder)
    {
        builder.ToTable("hub_users");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Username)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(x => x.Email)
            .IsRequired();

        builder.Property(x => x.EmailHash)
            .IsRequired();

        builder.Property(x => x.PasswordHash)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.AvatarUrl)
            .HasMaxLength(500);

        builder.Property(x => x.IsAdmin)
            .IsRequired();

        builder.Property(x => x.IsDisabled)
            .IsRequired();

        builder.Property(x => x.TwoFactorEnabled)
            .IsRequired();

        builder.Property(x => x.TwoFactorSecret)
            .HasMaxLength(255);

        builder.Property(x => x.SubscriptionTier)
            .IsRequired()
            .HasDefaultValue(BillingTier.Free);

        builder.Property(x => x.StripeCustomerId)
            .HasMaxLength(255);

        builder.Property(x => x.StripeSubscriptionId)
            .HasMaxLength(255);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.LastLoginAt);

        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => x.Username)
            .IsUnique();

        builder.HasIndex(x => x.EmailHash)
            .IsUnique();

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
