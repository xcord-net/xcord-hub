using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class BackupRecordConfiguration : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> builder)
    {
        builder.ToTable("backup_records");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.StoragePath)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.HasQueryFilter(x => x.DeletedAt == null);

        builder.HasOne(x => x.ManagedInstance)
            .WithMany(x => x.BackupRecords)
            .HasForeignKey(x => x.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ManagedInstanceId, x.StartedAt });
    }
}
