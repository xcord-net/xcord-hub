using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Infrastructure.Data;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef migrations add/update) when
/// the real DI container is not available. Supplies a dummy encryption key so the
/// EncryptedStringConverter can be configured without a running application.
/// </summary>
public sealed class HubDbContextFactory : IDesignTimeDbContextFactory<HubDbContext>
{
    public HubDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HubDbContext>();

        // Use an in-memory connection string placeholder — EF only needs to build the
        // model at design time; no real database connection is established.
        optionsBuilder.UseNpgsql("Host=localhost;Database=xcordhub_design;Username=postgres");

        // Dummy key — only used to satisfy the constructor; never contacts a DB.
        var encryptionService = new AesEncryptionService("design-time-dummy-key-not-used-in-production");

        return new HubDbContext(optionsBuilder.Options, encryptionService);
    }
}
