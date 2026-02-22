using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class ProvisioningEventConfiguration : IEntityTypeConfiguration<ProvisioningEvent>
{
    public void Configure(EntityTypeBuilder<ProvisioningEvent> builder)
    {
        builder.ToTable("provisioning_events");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ManagedInstanceId)
            .IsRequired();

        builder.Property(x => x.Phase)
            .IsRequired();

        builder.Property(x => x.StepName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.StartedAt);

        builder.Property(x => x.CompletedAt);

        builder.HasIndex(x => x.ManagedInstanceId);

        builder.HasOne(x => x.ManagedInstance)
            .WithMany(x => x.ProvisioningEvents)
            .HasForeignKey(x => x.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
