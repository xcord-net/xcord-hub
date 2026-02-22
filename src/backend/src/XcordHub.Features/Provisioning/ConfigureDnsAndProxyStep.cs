using Microsoft.EntityFrameworkCore;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub;

namespace XcordHub.Features.Provisioning;

public sealed class ConfigureDnsAndProxyStep : IProvisioningStep
{
    private readonly HubDbContext _dbContext;
    private readonly IDnsProvider _dnsProvider;
    private readonly ICaddyProxyManager _proxyManager;

    public string StepName => "ConfigureDnsAndProxy";

    public ConfigureDnsAndProxyStep(
        HubDbContext dbContext,
        IDnsProvider dnsProvider,
        ICaddyProxyManager proxyManager)
    {
        _dbContext = dbContext;
        _dnsProvider = dnsProvider;
        _proxyManager = proxyManager;
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

        try
        {
            // Create DNS A record
            var ipAddress = "127.0.0.1"; // Placeholder - would be the gateway's public IP
            await _dnsProvider.CreateARecordAsync(instance.Domain, ipAddress, cancellationToken);

            // Create Caddy proxy route using the deterministic container name
            // (Docker DNS resolves by container name, not container ID)
            var subdomain = instance.Domain.Split('.')[0];
            var containerName = $"xcord-{subdomain}-api";
            var routeId = await _proxyManager.CreateRouteAsync(instance.Domain, containerName, cancellationToken);

            // Store route ID
            instance.Infrastructure.CaddyRouteId = routeId;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            return Error.Failure("DNS_PROXY_FAILED", $"Failed to configure DNS/proxy: {ex.Message}");
        }
    }

    public async Task<Result<bool>> VerifyAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _dbContext.ManagedInstances
            .Include(i => i.Infrastructure)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance?.Infrastructure == null)
        {
            return Error.NotFound("INFRASTRUCTURE_NOT_FOUND", $"Infrastructure for instance {instanceId} not found");
        }

        try
        {
            // Verify DNS record
            var dnsOk = await _dnsProvider.VerifyDnsRecordAsync(instance.Domain, cancellationToken);
            if (!dnsOk)
            {
                return Error.Failure("DNS_VERIFY_FAILED", "DNS record verification failed");
            }

            // Verify Caddy route
            var routeOk = await _proxyManager.VerifyRouteAsync(instance.Infrastructure.CaddyRouteId, cancellationToken);
            if (!routeOk)
            {
                return Error.Failure("PROXY_VERIFY_FAILED", "Proxy route verification failed");
            }

            return true;
        }
        catch (Exception ex)
        {
            return Error.Failure("DNS_PROXY_VERIFY_ERROR", $"DNS/proxy verification error: {ex.Message}");
        }
    }
}
