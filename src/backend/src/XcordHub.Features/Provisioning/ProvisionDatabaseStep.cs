using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using XcordHub.Infrastructure.Data;
using XcordHub;

namespace XcordHub.Features.Provisioning;

/// <summary>
/// Creates a PostgreSQL database for the provisioned xcord-fed instance.
/// Connects to the shared gateway-pg instance using the hub's superuser
/// credentials and executes CREATE DATABASE for the instance's database name.
/// The instance API container then connects to this database at runtime.
/// </summary>
public sealed class ProvisionDatabaseStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;
    private readonly string _hubConnectionString;

    public string StepName => "ProvisionDatabase";

    public ProvisionDatabaseStep(HubDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _hubConnectionString = configuration.GetSection("Database:ConnectionString").Value
            ?? throw new InvalidOperationException("Database:ConnectionString not configured");
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

        var dbName = instance.Infrastructure.DatabaseName;

        try
        {
            // Connect to the "postgres" maintenance database using the hub's superuser credentials
            var builder = new NpgsqlConnectionStringBuilder(_hubConnectionString)
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
                // CREATE DATABASE cannot run inside a transaction, so we use a raw command
                await using var createCmd = new NpgsqlCommand(
                    $"CREATE DATABASE \"{dbName}\"", conn);
                await createCmd.ExecuteNonQueryAsync(cancellationToken);
            }

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
            var builder = new NpgsqlConnectionStringBuilder(_hubConnectionString)
            {
                Database = "postgres"
            };

            await using var conn = new NpgsqlConnection(builder.ConnectionString);
            await conn.OpenAsync(cancellationToken);

            await using var checkCmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @name", conn);
            checkCmd.Parameters.AddWithValue("name", infrastructure.DatabaseName);
            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken);

            return exists != null
                ? true
                : Error.Failure("DB_NOT_FOUND", $"Database '{infrastructure.DatabaseName}' not found after creation");
        }
        catch (Exception ex)
        {
            return Error.Failure("DB_VERIFY_FAILED", $"Database verification failed: {ex.Message}");
        }
    }
}
