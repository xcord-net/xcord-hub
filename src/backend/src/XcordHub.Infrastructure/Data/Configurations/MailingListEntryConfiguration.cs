using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class MailingListEntryConfiguration : IEntityTypeConfiguration<MailingListEntry>
{
    public void Configure(EntityTypeBuilder<MailingListEntry> builder)
    {
        builder.ToTable("mailing_list_entries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.Tier)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => new { x.Email, x.Tier })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
