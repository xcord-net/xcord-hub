using System.Text.Json.Serialization;

namespace XcordHub.Entities;

public sealed class BackupPolicy
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public bool Enabled { get; set; } = true;
    public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;
    public int RetentionDays { get; set; } = 30;
    public bool BackupDatabase { get; set; } = true;
    public bool BackupFiles { get; set; } = true;
    public bool BackupRedis { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ManagedInstance ManagedInstance { get; set; } = null!;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackupFrequency
{
    Hourly,
    Daily,
    Weekly
}
