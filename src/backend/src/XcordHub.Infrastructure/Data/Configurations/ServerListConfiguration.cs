using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class ServerListConfiguration : IEntityTypeConfiguration<ServerList>
{
    public void Configure(EntityTypeBuilder<ServerList> builder)
    {
        builder.ToTable("server_lists");

        builder.HasKey(s => s.HubKey);

        builder.Property(s => s.HubKey)
            .HasMaxLength(64);

        builder.Property(s => s.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("now()");
    }
}

public sealed class ServerListEntryConfiguration : IEntityTypeConfiguration<ServerListEntry>
{
    public void Configure(EntityTypeBuilder<ServerListEntry> builder)
    {
        builder.ToTable("server_list_entries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        builder.Property(e => e.HubKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.ServerUrl)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(e => e.ServerName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.ServerIconUrl)
            .HasMaxLength(512);

        builder.Property(e => e.AddedAt)
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.HasOne(e => e.ServerList)
            .WithMany(s => s.Entries)
            .HasForeignKey(e => e.HubKey)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.HubKey, e.ServerUrl })
            .IsUnique();
    }
}
