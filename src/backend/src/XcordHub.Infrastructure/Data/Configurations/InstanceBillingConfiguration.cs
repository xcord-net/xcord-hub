using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class InstanceBillingConfiguration : IEntityTypeConfiguration<InstanceBilling>
{
    public void Configure(EntityTypeBuilder<InstanceBilling> builder)
    {
        builder.ToTable("instance_billing");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ManagedInstanceId)
            .IsRequired();

        builder.Property(x => x.Tier)
            .IsRequired();

        builder.Property(x => x.BillingStatus)
            .IsRequired();

        builder.Property(x => x.BillingExempt)
            .IsRequired();

        builder.Property(x => x.StripePriceId)
            .HasMaxLength(255);

        builder.Property(x => x.StripeSubscriptionId)
            .HasMaxLength(255);

        builder.Property(x => x.CurrentPeriodEnd);

        builder.Property(x => x.NextBillingDate);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.ManagedInstanceId)
            .IsUnique();

        builder.HasOne(x => x.ManagedInstance)
            .WithOne(x => x.Billing)
            .HasForeignKey<InstanceBilling>(x => x.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
