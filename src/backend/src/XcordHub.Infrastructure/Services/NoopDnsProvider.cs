namespace XcordHub.Infrastructure.Services;

public sealed class NoopDnsProvider : IDnsProvider
{
    public Task CreateARecordAsync(string subdomain, string ipAddress, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<bool> VerifyDnsRecordAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task DeleteARecordAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
