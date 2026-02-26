using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XcordHub.Entities;

namespace XcordHub.Infrastructure.Data.Configurations;

public sealed class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        builder.ToTable("login_attempts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.IpAddress)
            .IsRequired()
            .HasMaxLength(45); // IPv6 max length

        builder.Property(x => x.UserAgent)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(x => x.Success)
            .IsRequired();

        builder.Property(x => x.FailureReason)
            .HasMaxLength(255);

        builder.Property(x => x.UserId);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.IpAddress);
    }
}
