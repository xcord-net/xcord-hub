using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="EncryptedDataKey"/>.
/// </summary>
public sealed class EncryptedDataKeyConfiguration : IEntityTypeConfiguration<EncryptedDataKey>
{
    public void Configure(EntityTypeBuilder<EncryptedDataKey> builder)
    {
        builder.ToTable("encrypted_data_keys");

        builder.HasKey(k => k.Version);

        builder.Property(k => k.Version)
            .ValueGeneratedNever()
            .HasColumnType("integer");

        builder.Property(k => k.WrappedKey)
            .IsRequired()
            .HasColumnType("bytea");

        builder.Property(k => k.IsActive)
            .IsRequired();

        builder.Property(k => k.CreatedAt)
            .IsRequired();

        builder.HasIndex(k => k.IsActive)
            .IsUnique()
            .HasFilter("\"IsActive\" = true")
            .HasDatabaseName("IX_encrypted_data_keys_IsActive_Unique");
    }
}
