using System.Diagnostics.Metrics;

namespace XcordHub.Infrastructure.Services;

public sealed class ProvisioningMetrics
{
    private readonly Meter _meter;
    private readonly Histogram<double> _provisioningDurationSeconds;
    private readonly Counter<long> _provisioningStepsTotal;

    public ProvisioningMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create("XcordHub.Provisioning");

        _provisioningDurationSeconds = _meter.CreateHistogram<double>(
            "provisioning_duration_seconds",
            unit: "s",
            description: "Duration of instance provisioning in seconds");

        _provisioningStepsTotal = _meter.CreateCounter<long>(
            "provisioning_steps_total",
            description: "Total number of provisioning steps executed");
    }

    public void RecordProvisioningDuration(double durationSeconds)
    {
        _provisioningDurationSeconds.Record(durationSeconds);
    }

    public void RecordProvisioningStep(string stepName, bool success)
    {
        _provisioningStepsTotal.Add(1, new KeyValuePair<string, object?>("step", stepName), new KeyValuePair<string, object?>("success", success));
    }
}
