using Microsoft.Extensions.Logging;

namespace XcordHub.Infrastructure.Data;

public sealed class NoopDatabaseManager : IDatabaseManager
{
    private readonly ILogger<NoopDatabaseManager> _logger;

    public NoopDatabaseManager(ILogger<NoopDatabaseManager> logger)
    {
        _logger = logger;
    }

    public Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would drop database {DatabaseName}", databaseName);
        return Task.CompletedTask;
    }

    public Task<bool> VerifyDatabaseExistsAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NOOP: Would verify database {DatabaseName} exists", databaseName);
        return Task.FromResult(true);
    }
}
