using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class InstanceHealthConfiguration : IEntityTypeConfiguration<InstanceHealth>
{
    public void Configure(EntityTypeBuilder<InstanceHealth> builder)
    {
        builder.ToTable("instance_health");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ManagedInstanceId)
            .IsRequired();

        builder.Property(x => x.IsHealthy)
            .IsRequired();

        builder.Property(x => x.LastCheckAt)
            .IsRequired();

        builder.Property(x => x.ConsecutiveFailures)
            .IsRequired();

        builder.Property(x => x.ResponseTimeMs);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(1000);

        builder.HasIndex(x => x.ManagedInstanceId)
            .IsUnique();

        builder.HasOne(x => x.ManagedInstance)
            .WithOne(x => x.Health)
            .HasForeignKey<InstanceHealth>(x => x.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
