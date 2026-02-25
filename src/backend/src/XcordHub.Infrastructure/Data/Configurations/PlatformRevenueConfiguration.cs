using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class PlatformRevenueConfiguration : IEntityTypeConfiguration<PlatformRevenue>
{
    public void Configure(EntityTypeBuilder<PlatformRevenue> builder)
    {
        builder.ToTable("platform_revenues");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.StripeTransferId).HasMaxLength(255);
        builder.Property(r => r.Currency).IsRequired().HasMaxLength(3);
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.HasOne(r => r.ManagedInstance).WithMany().HasForeignKey(r => r.ManagedInstanceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.ManagedInstanceId);
        builder.HasIndex(r => new { r.ManagedInstanceId, r.PeriodStart, r.PeriodEnd });
    }
}
