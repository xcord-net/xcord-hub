namespace XcordHub.Entities;

/// <summary>
/// Records a contiguous uptime interval for a managed instance.
/// Intervals are opened when the instance transitions to healthy and closed when it becomes unhealthy.
/// Used as the source of truth for usage-based billing on Enterprise metered plans.
/// </summary>
public sealed class UptimeInterval : ISoftDeletable
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }

    /// <summary>Timestamp when the instance became healthy and this interval started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Timestamp when the instance became unhealthy and the interval ended. Null while still running.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Whether this interval has been reported to Stripe as a usage record.</summary>
    public bool ReportedToStripe { get; set; }

    /// <summary>When this interval was reported to Stripe (if applicable).</summary>
    public DateTimeOffset? ReportedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigation properties
    public ManagedInstance ManagedInstance { get; set; } = null!;

    /// <summary>Duration in minutes. Returns null if the interval is still open.</summary>
    public double? DurationMinutes =>
        EndedAt.HasValue ? (EndedAt.Value - StartedAt).TotalMinutes : null;
}
