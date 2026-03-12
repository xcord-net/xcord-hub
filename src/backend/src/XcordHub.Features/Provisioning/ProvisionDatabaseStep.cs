using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub;

namespace XcordHub.Features.Provisioning;

/// <summary>
/// Creates a PostgreSQL database and a dedicated per-instance user for the provisioned
/// xcord-fed instance. The per-instance user can only access its own database, preventing
/// cross-instance data access even if an instance is compromised.
/// </summary>
public sealed class ProvisionDatabaseStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;
    private readonly string _hubConnectionString;
    private readonly TopologyResolver _resolver;
    private readonly ILogger<ProvisionDatabaseStep> _logger;

    public string StepName => "ProvisionDatabase";

    public ProvisionDatabaseStep(HubDbContext dbContext, IConfiguration configuration, TopologyResolver resolver, ILogger<ProvisionDatabaseStep> logger)
    {
        _dbContext = dbContext;
        _hubConnectionString = configuration.GetSection("Database:ConnectionString").Value
            ?? throw new InvalidOperationException("Database:ConnectionString not configured");
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<Result<bool>> ExecuteAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance?.Infrastructure == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found");
        }

        var infra = instance.Infrastructure;
        var dbName = infra.DatabaseName;
        var subdomain = ValidationHelpers.ExtractSubdomain(instance.Domain);
        var dbUsername = $"xcord_{subdomain}";

        try
        {
            // Resolve PG connection string: data pool > compute pool > hub fallback
            var poolConnStr = _resolver.GetDatabaseConnectionString(infra.PlacedInPool, infra.PlacedInDataPool);
            var connectionString = poolConnStr ?? _hubConnectionString;

            // Connect to the "postgres" maintenance database using the hub's superuser credentials
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = "postgres"
            };

            await using var conn = new NpgsqlConnection(builder.ConnectionString);
            await conn.OpenAsync(cancellationToken);

            // Check if database already exists
            await using var checkCmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @name", conn);
            checkCmd.Parameters.AddWithValue("name", dbName);
            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);

            if (exists == null)
            {
                // CREATE DATABASE cannot run inside a transaction
                await using var createDbCmd = new NpgsqlCommand(
                    $"CREATE DATABASE \"{dbName}\"", conn);
                await createDbCmd.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Created database {Database} for instance {Domain}", dbName, instance.Domain);
            }

            // Create per-instance PG user (idempotent — skip if exists)
            await using var checkUserCmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_roles WHERE rolname = @name", conn);
            checkUserCmd.Parameters.AddWithValue("name", dbUsername);
            var userExists = await checkUserCmd.ExecuteScalarAsync(cancellationToken);

            if (userExists == null)
            {
                // Use the already-generated DatabasePassword from GenerateSecretsStep.
                // The password is stored encrypted in the hub DB — we decrypt it here
                // to pass to CREATE USER (PG requires the plaintext password).
                var dbPassword = infra.DatabasePassword;

                // CREATE USER with LOGIN and restricted connection privileges
                await using var createUserCmd = new NpgsqlCommand(
                    $"CREATE USER \"{dbUsername}\" WITH PASSWORD '{EscapeSqlString(dbPassword)}'", conn);
                await createUserCmd.ExecuteNonQueryAsync(cancellationToken);

                await using var grantConnectCmd = new NpgsqlCommand(
                    $"GRANT CONNECT ON DATABASE \"{dbName}\" TO \"{dbUsername}\"", conn);
                await grantConnectCmd.ExecuteNonQueryAsync(cancellationToken);

                _logger.LogInformation("Created PG user {Username} for instance {Domain}", dbUsername, instance.Domain);
            }

            // Connect to the instance database to grant schema privileges
            var instanceBuilder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = dbName
            };

            await using var instanceConn = new NpgsqlConnection(instanceBuilder.ConnectionString);
            await instanceConn.OpenAsync(cancellationToken);

            // Create extensions that require superuser privileges. The per-instance
            // user cannot create extensions, so we do it here as the hub superuser.
            await using var extCmd = new NpgsqlCommand(
                "CREATE EXTENSION IF NOT EXISTS \"pgcrypto\"", instanceConn);
            await extCmd.ExecuteNonQueryAsync(cancellationToken);

            // Grant schema privileges (idempotent — GRANT is safe to repeat)
            var grantStatements = new[]
            {
                $"GRANT ALL ON SCHEMA public TO \"{dbUsername}\"",
                $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO \"{dbUsername}\"",
                $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO \"{dbUsername}\""
            };

            foreach (var sql in grantStatements)
            {
                await using var grantCmd = new NpgsqlCommand(sql, instanceConn);
                await grantCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Store the username on the infrastructure record
            infra.DatabaseUsername = dbUsername;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            return Error.Failure("DB_PROVISION_FAILED", $"Failed to create database '{dbName}': {ex.Message}");
        }
    }

    public async Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var infrastructure = await _dbContext.InstanceInfrastructures
            .FirstOrDefaultAsync(i => i.ManagedInstanceId == instanceId, cancellationToken);

        if (infrastructure == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found");
        }

        try
        {
            // Resolve PG connection string: data pool > compute pool > hub fallback
            var poolConnStr = _resolver.GetDatabaseConnectionString(infrastructure.PlacedInPool, infrastructure.PlacedInDataPool);
            var connectionString = poolConnStr ?? _hubConnectionString;

            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = "postgres"
            };

            await using var conn = new NpgsqlConnection(builder.ConnectionString);
            await conn.OpenAsync(cancellationToken);

            // Verify database exists
            await using var checkCmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @name", conn);
            checkCmd.Parameters.AddWithValue("name", infrastructure.DatabaseName);
            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);

            if (exists == null)
            {
                return Error.Failure("DB_NOT_FOUND", $"Database '{infrastructure.DatabaseName}' not found after creation");
            }

            // Verify per-instance user exists
            if (!string.IsNullOrWhiteSpace(infrastructure.DatabaseUsername))
            {
                await using var checkUserCmd = new NpgsqlCommand(
                    "SELECT 1 FROM pg_roles WHERE rolname = @name", conn);
                checkUserCmd.Parameters.AddWithValue("name", infrastructure.DatabaseUsername);
                var userExists = await checkUserCmd.ExecuteScalarAsync(cancellationToken);

                if (userExists == null)
                {
                    return Error.Failure("DB_USER_NOT_FOUND",
                        $"Per-instance user '{infrastructure.DatabaseUsername}' not found");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            return Error.Failure("DB_VERIFY_FAILED", $"Database verification failed: {ex.Message}");
        }
    }

    private static string EscapeSqlString(string value)
    {
        return value.Replace("'", "''");
    }
}
