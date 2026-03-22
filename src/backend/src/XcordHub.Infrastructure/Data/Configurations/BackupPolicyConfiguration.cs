using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class BackupPolicyConfiguration : IEntityTypeConfiguration<BackupPolicy>
{
    public void Configure(EntityTypeBuilder<BackupPolicy> builder)
    {
        builder.ToTable("backup_policies");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Frequency)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(x => x.ManagedInstance)
            .WithOne(x => x.BackupPolicy)
            .HasForeignKey<BackupPolicy>(x => x.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ManagedInstanceId)
            .IsUnique();

        builder.HasQueryFilter(x => x.ManagedInstance!.DeletedAt == null);
    }
}
