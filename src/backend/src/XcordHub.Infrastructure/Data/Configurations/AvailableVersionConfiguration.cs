using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class AvailableVersionConfiguration : IEntityTypeConfiguration<AvailableVersion>
{
    public void Configure(EntityTypeBuilder<AvailableVersion> builder)
    {
        builder.ToTable("available_versions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Version)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Image)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.ReleaseNotes)
            .HasMaxLength(4000);

        builder.Property(x => x.IsMinimumVersion)
            .IsRequired();

        builder.Property(x => x.MinimumEnforcementDate);

        builder.Property(x => x.PublishedAt)
            .IsRequired();

        builder.Property(x => x.PublishedBy)
            .IsRequired();

        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => x.Version)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        builder.HasIndex(x => x.PublishedBy);

        builder.HasOne(x => x.Publisher)
            .WithMany()
            .HasForeignKey(x => x.PublishedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
