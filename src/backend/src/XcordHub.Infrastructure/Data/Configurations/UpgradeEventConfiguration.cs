using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class UpgradeEventConfiguration : IEntityTypeConfiguration<UpgradeEvent>
{
    public void Configure(EntityTypeBuilder<UpgradeEvent> builder)
    {
        builder.ToTable("upgrade_events");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UpgradeRolloutId);

        builder.Property(x => x.ManagedInstanceId)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.PreviousImage)
            .HasMaxLength(500);

        builder.Property(x => x.TargetImage)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.PreviousVersion)
            .HasMaxLength(50);

        builder.Property(x => x.NewVersion)
            .HasMaxLength(50);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.StartedAt);

        builder.Property(x => x.CompletedAt);

        builder.HasIndex(x => x.ManagedInstanceId);

        builder.HasIndex(x => x.UpgradeRolloutId);

        builder.HasQueryFilter(x => x.ManagedInstance!.DeletedAt == null);

        builder.HasOne(x => x.Rollout)
            .WithMany(x => x.UpgradeEvents)
            .HasForeignKey(x => x.UpgradeRolloutId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ManagedInstance)
            .WithMany(x => x.UpgradeEvents)
            .HasForeignKey(x => x.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
