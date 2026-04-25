namespace XcordHub.Entities;

public sealed class SystemConfig
{
    public const long SingletonId = 1;

    public long Id { get; set; }
    public bool PaidServersDisabled { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
