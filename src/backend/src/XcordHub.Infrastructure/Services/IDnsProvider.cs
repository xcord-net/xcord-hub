namespace XcordHub.Infrastructure.Services;

public interface IDnsProvider
{
    Task CreateARecordAsync(string subdomain, string ipAddress, CancellationToken cancellationToken = default);
    Task<bool> VerifyDnsRecordAsync(string subdomain, CancellationToken cancellationToken = default);
    Task DeleteARecordAsync(string subdomain, CancellationToken cancellationToken = default);
}
