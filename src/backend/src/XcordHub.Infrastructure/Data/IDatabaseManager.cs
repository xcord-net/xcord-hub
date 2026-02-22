namespace XcordHub.Infrastructure.Data;

public interface IDatabaseManager
{
    Task DropDatabaseAsync(string databaseName, CancellationToken cancellationToken = default);
    Task<bool> VerifyDatabaseExistsAsync(string databaseName, CancellationToken cancellationToken = default);
}
