namespace XcordHub.Entities;

public sealed class InstanceSecrets
{
    public long Id { get; set; }
    public long ManagedInstanceId { get; set; }
    public byte[] EncryptedPayload { get; set; } = Array.Empty<byte>();
    public byte[] Nonce { get; set; } = Array.Empty<byte>();
    public string? BootstrapTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public ManagedInstance ManagedInstance { get; set; } = null!;
}
