using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class InstanceConfigConfiguration : IEntityTypeConfiguration<InstanceConfig>
{
    public void Configure(EntityTypeBuilder<InstanceConfig> builder)
    {
        builder.ToTable("instance_configs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ManagedInstanceId)
            .IsRequired();

        builder.Property(x => x.ConfigJson)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.ResourceLimitsJson)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.FeatureFlagsJson)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.Version)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasIndex(x => x.ManagedInstanceId)
            .IsUnique();

        builder.HasOne(x => x.ManagedInstance)
            .WithOne(x => x.Config)
            .HasForeignKey<InstanceConfig>(x => x.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
