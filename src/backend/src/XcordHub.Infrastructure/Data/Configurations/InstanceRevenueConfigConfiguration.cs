using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class InstanceRevenueConfigConfiguration : IEntityTypeConfiguration<InstanceRevenueConfig>
{
    public void Configure(EntityTypeBuilder<InstanceRevenueConfig> builder)
    {
        builder.ToTable("instance_revenue_configs");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.StripeConnectedAccountId).HasMaxLength(255);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.HasOne(c => c.ManagedInstance).WithMany().HasForeignKey(c => c.ManagedInstanceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => c.ManagedInstanceId).IsUnique();
    }
}
