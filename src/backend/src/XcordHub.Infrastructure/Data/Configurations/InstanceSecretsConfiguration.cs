using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class InstanceSecretsConfiguration : IEntityTypeConfiguration<InstanceSecrets>
{
    public void Configure(EntityTypeBuilder<InstanceSecrets> builder)
    {
        builder.ToTable("instance_secrets");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ManagedInstanceId)
            .IsRequired();

        builder.Property(x => x.EncryptedPayload)
            .IsRequired();

        builder.Property(x => x.Nonce)
            .IsRequired();

        builder.Property(x => x.BootstrapTokenHash)
            .HasMaxLength(64);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.ManagedInstanceId)
            .IsUnique();

        builder.HasOne(x => x.ManagedInstance)
            .WithOne(x => x.Secrets)
            .HasForeignKey<InstanceSecrets>(x => x.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
