namespace XcordHub.Entities;

public sealed class ServerList
{
    public string HubKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<ServerListEntry> Entries { get; set; } = new List<ServerListEntry>();
}

public sealed class ServerListEntry
{
    public long Id { get; set; }
    public string HubKey { get; set; } = string.Empty;
    public string ServerUrl { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string? ServerIconUrl { get; set; }
    public DateTimeOffset AddedAt { get; set; }

    public ServerList ServerList { get; set; } = null!;
}
