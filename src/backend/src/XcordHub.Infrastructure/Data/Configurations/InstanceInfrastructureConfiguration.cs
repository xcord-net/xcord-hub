using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data.Converters;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class InstanceInfrastructureConfiguration : IEntityTypeConfiguration<InstanceInfrastructure>
{
    private readonly EncryptedStringConverter _encryptedStringConverter;

    public InstanceInfrastructureConfiguration(IEncryptionService encryptionService)
    {
        _encryptedStringConverter = new EncryptedStringConverter(encryptionService);
    }

    public void Configure(EntityTypeBuilder<InstanceInfrastructure> builder)
    {
        builder.ToTable("instance_infrastructure");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ManagedInstanceId)
            .IsRequired();

        builder.Property(x => x.DockerNetworkId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.DockerContainerId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.DatabaseName)
            .IsRequired()
            .HasMaxLength(255);

        // Sensitive credential — stored encrypted as bytea
        builder.Property(x => x.DatabasePassword)
            .IsRequired()
            .HasColumnType("bytea")
            .HasConversion(_encryptedStringConverter);

        builder.Property(x => x.RedisDb)
            .IsRequired();

        // Access key is not secret — stored as plaintext
        builder.Property(x => x.MinioAccessKey)
            .IsRequired()
            .HasMaxLength(255);

        // Secret key is sensitive — stored encrypted as bytea
        builder.Property(x => x.MinioSecretKey)
            .IsRequired()
            .HasColumnType("bytea")
            .HasConversion(_encryptedStringConverter);

        builder.Property(x => x.CaddyRouteId)
            .IsRequired()
            .HasMaxLength(255);

        // API key is not secret — stored as plaintext
        builder.Property(x => x.LiveKitApiKey)
            .IsRequired()
            .HasMaxLength(255);

        // Secret key is sensitive — stored encrypted as bytea
        builder.Property(x => x.LiveKitSecretKey)
            .IsRequired()
            .HasColumnType("bytea")
            .HasConversion(_encryptedStringConverter);

        // Per-instance KEK — stored encrypted so only the hub can decrypt it
        builder.Property(x => x.InstanceKek)
            .IsRequired()
            .HasColumnType("bytea")
            .HasConversion(_encryptedStringConverter);

        builder.Property(x => x.BootstrapTokenHash)
            .HasMaxLength(64);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.ManagedInstanceId)
            .IsUnique();

        builder.HasOne(x => x.ManagedInstance)
            .WithOne(x => x.Infrastructure)
            .HasForeignKey<InstanceInfrastructure>(x => x.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
