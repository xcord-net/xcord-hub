using System.Text.Json.Serialization;

namespace XcordHub.Entities;

public sealed class BackupRecord
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public BackupStatus Status { get; set; }
    public BackupKind Kind { get; set; }
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ManagedInstance ManagedInstance { get; set; } = null!;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackupStatus
{
    InProgress,
    Completed,
    Failed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackupKind
{
    Database,
    Files,
    Redis,
    Full
}
