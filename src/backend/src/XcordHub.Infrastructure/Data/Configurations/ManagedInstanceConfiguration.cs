using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class ManagedInstanceConfiguration : IEntityTypeConfiguration<ManagedInstance>
{
    public void Configure(EntityTypeBuilder<ManagedInstance> builder)
    {
        builder.ToTable("managed_instances");

        builder.HasKey(x => x.Id);

        // Use PostgreSQL xmin system column as optimistic concurrency token.
        // xmin is updated automatically by Postgres on every row write, so
        // concurrent modifications will cause a DbUpdateConcurrencyException.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();

        builder.Property(x => x.OwnerId)
            .IsRequired();

        builder.Property(x => x.Domain)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.Description)
            .HasMaxLength(1000);

        builder.Property(x => x.IconUrl)
            .HasMaxLength(500);

        builder.Property(x => x.MemberCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.OnlineCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.SnowflakeWorkerId)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => x.Domain)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        builder.HasIndex(x => x.SnowflakeWorkerId)
            .IsUnique()
            .HasFilter("\"SnowflakeWorkerId\" > 0 AND \"DeletedAt\" IS NULL");

        builder.HasIndex(x => x.OwnerId);

        builder.HasOne(x => x.Owner)
            .WithMany(x => x.ManagedInstances)
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
