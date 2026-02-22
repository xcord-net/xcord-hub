using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class WorkerIdRegistryConfiguration : IEntityTypeConfiguration<WorkerIdRegistry>
{
    public void Configure(EntityTypeBuilder<WorkerIdRegistry> builder)
    {
        builder.ToTable("worker_id_registry");

        builder.HasKey(w => w.WorkerId);

        builder.Property(w => w.WorkerId)
            .HasColumnName("worker_id")
            .ValueGeneratedNever();

        builder.Property(w => w.ManagedInstanceId)
            .HasColumnName("managed_instance_id")
            .IsRequired(false);

        builder.Property(w => w.IsTombstoned)
            .HasColumnName("is_tombstoned")
            .IsRequired();

        builder.Property(w => w.AllocatedAt)
            .HasColumnName("allocated_at")
            .IsRequired();

        builder.Property(w => w.ReleasedAt)
            .HasColumnName("released_at")
            .IsRequired(false);

        builder.HasOne(w => w.ManagedInstance)
            .WithMany()
            .HasForeignKey(w => w.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(w => w.ManagedInstanceId);
        builder.HasIndex(w => w.IsTombstoned);
    }
}
