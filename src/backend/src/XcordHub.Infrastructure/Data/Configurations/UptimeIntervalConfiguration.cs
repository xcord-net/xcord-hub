using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class UptimeIntervalConfiguration : IEntityTypeConfiguration<UptimeInterval>
{
    public void Configure(EntityTypeBuilder<UptimeInterval> builder)
    {
        builder.ToTable("uptime_intervals");

        builder.HasKey(x => x.Id);

        NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(builder.Property(x => x.Id));

        builder.Property(x => x.ManagedInstanceId)
            .IsRequired();

        builder.Property(x => x.StartedAt)
            .IsRequired();

        builder.Property(x => x.EndedAt);

        builder.Property(x => x.ReportedToStripe)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.ReportedAt);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.DeletedAt);

        // Index for querying open intervals per instance
        builder.HasIndex(x => new { x.ManagedInstanceId, x.EndedAt });

        // Index for finding unreported closed intervals
        builder.HasIndex(x => new { x.ReportedToStripe, x.EndedAt });

        builder.HasQueryFilter(x => x.DeletedAt == null);

        builder.HasOne(x => x.ManagedInstance)
            .WithMany(x => x.UptimeIntervals)
            .HasForeignKey(x => x.ManagedInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
