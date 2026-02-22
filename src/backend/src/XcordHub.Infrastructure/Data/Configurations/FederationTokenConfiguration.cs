using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class FederationTokenConfiguration : IEntityTypeConfiguration<FederationToken>
{
    public void Configure(EntityTypeBuilder<FederationToken> builder)
    {
        builder.ToTable("federation_tokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.ManagedInstanceId)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.RevokedAt);

        builder.HasIndex(x => x.TokenHash)
            .IsUnique();

        builder.HasIndex(x => x.ManagedInstanceId);

        builder.HasOne(x => x.ManagedInstance)
            .WithMany(x => x.FederationTokens)
            .HasForeignKey(x => x.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
