using System.Diagnostics.Metrics;

namespace XcordHub.Infrastructure.Services;

public sealed class GatewayMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _instancesProvisionedTotal;
    private readonly Counter<long> _healthChecksTotal;
    private readonly Counter<long> _healthCheckFailuresTotal;

    public GatewayMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create("XcordHub.Gateway");

        _instancesProvisionedTotal = _meter.CreateCounter<long>(
            "instances_provisioned_total",
            description: "Total number of instances provisioned");

        _healthChecksTotal = _meter.CreateCounter<long>(
            "health_checks_total",
            description: "Total number of health checks performed");

        _healthCheckFailuresTotal = _meter.CreateCounter<long>(
            "health_check_failures_total",
            description: "Total number of health check failures");
    }

    public void RecordInstanceProvisioned()
    {
        _instancesProvisionedTotal.Add(1);
    }

    public void RecordHealthCheck(bool success)
    {
        _healthChecksTotal.Add(1);
        if (!success)
        {
            _healthCheckFailuresTotal.Add(1);
        }
    }
}
