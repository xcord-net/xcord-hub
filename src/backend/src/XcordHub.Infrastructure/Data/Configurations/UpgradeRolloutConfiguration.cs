using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class UpgradeRolloutConfiguration : IEntityTypeConfiguration<UpgradeRollout>
{
    public void Configure(EntityTypeBuilder<UpgradeRollout> builder)
    {
        builder.ToTable("upgrade_rollouts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FromImage)
            .HasMaxLength(500);

        builder.Property(x => x.ToImage)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.TotalInstances)
            .IsRequired();

        builder.Property(x => x.CompletedInstances)
            .IsRequired();

        builder.Property(x => x.FailedInstanceId);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.TargetPool)
            .HasMaxLength(255);

        builder.Property(x => x.StartedAt)
            .IsRequired();

        builder.Property(x => x.CompletedAt);

        builder.Property(x => x.InitiatedBy)
            .IsRequired();

        builder.HasIndex(x => x.InitiatedBy);

        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Initiator)
            .WithMany()
            .HasForeignKey(x => x.InitiatedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
