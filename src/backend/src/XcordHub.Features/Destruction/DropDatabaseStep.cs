using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using XcordHub.Entities;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Destruction;

/// <summary>
/// Drops the instance's PostgreSQL database and per-instance user during destruction.
/// Terminates all existing connections before dropping so the DROP DATABASE succeeds
/// even if a stale connection is still open. Uses DROP DATABASE IF EXISTS and DROP ROLE
/// IF EXISTS so the step is idempotent (safe to re-run on partially-destroyed instances).
/// </summary>
public sealed class DropDatabaseStep : IDestructionStep
{
    private readonly string _hubConnectionString;
    private readonly TopologyResolver _resolver;
    private readonly ILogger<DropDatabaseStep> _logger;

    public string StepName => "DropDatabase";

    public DropDatabaseStep(IConfiguration configuration, TopologyResolver resolver, ILogger<DropDatabaseStep> logger)
    {
        _hubConnectionString = configuration.GetSection("Database:ConnectionString").Value
            ?? throw new InvalidOperationException("Database:ConnectionString not configured");
        _resolver = resolver;
        _logger = logger;
    }

    public async Task ExecuteAsync(ManagedInstance instance, InstanceInfrastructure infrastructure, CancellationToken cancellationToken)
    {
        var dbName = infrastructure.DatabaseName;
        var dbUsername = infrastructure.DatabaseUsername;

        if (string.IsNullOrWhiteSpace(dbName))
        {
            _logger.LogWarning("No DatabaseName set for instance {Domain}, skipping DropDatabase", instance.Domain);
            return;
        }

        try
        {
            var poolConnStr = _resolver.GetDatabaseConnectionString(infrastructure.PlacedInPool, infrastructure.PlacedInDataPool);
            var connectionString = poolConnStr ?? _hubConnectionString;

            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = "postgres"
            };

            await using var conn = new NpgsqlConnection(builder.ConnectionString);
            await conn.OpenAsync(cancellationToken);

            // Terminate all existing connections to the database before dropping it.
            // pg_terminate_backend returns false for connections that cannot be terminated
            // (e.g. the current superuser connection) but does not throw, so this is safe.
            await using var terminateCmd = new NpgsqlCommand(
                """
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @name AND pid <> pg_backend_pid()
                """, conn);
            terminateCmd.Parameters.AddWithValue("name", dbName);
            await terminateCmd.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Dropping database {Database} for instance {Domain}", dbName, instance.Domain);

            await using var dropDbCmd = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{EscapeIdentifier(dbName)}\"", conn);
            await dropDbCmd.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Dropped database {Database} for instance {Domain}", dbName, instance.Domain);

            // Drop the per-instance PG user if one was created.
            if (!string.IsNullOrWhiteSpace(dbUsername))
            {
                await using var dropRoleCmd = new NpgsqlCommand(
                    $"DROP ROLE IF EXISTS \"{EscapeIdentifier(dbUsername)}\"", conn);
                await dropRoleCmd.ExecuteNonQueryAsync(cancellationToken);

                _logger.LogInformation("Dropped PG role {Username} for instance {Domain}", dbUsername, instance.Domain);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DropDatabase step failed for instance {Domain} (db: {Database}) - continuing cleanup",
                instance.Domain, dbName);
        }
    }

    /// <summary>
    /// Escapes a PostgreSQL identifier by doubling any double-quote characters.
    /// This prevents SQL injection in identifier contexts where parameterization is not supported.
    /// </summary>
    private static string EscapeIdentifier(string identifier) => identifier.Replace("\"", "\"\"");
}
