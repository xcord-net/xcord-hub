using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core entity configuration for SystemSetting.
/// </summary>
public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("system_settings");

        builder.HasKey(ss => ss.Key);
        builder.Property(ss => ss.Key)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(ss => ss.Value)
            .IsRequired()
            .HasMaxLength(8000);

        builder.Property(ss => ss.CreatedAt)
            .IsRequired();

        builder.Property(ss => ss.UpdatedAt)
            .IsRequired();
    }
}
