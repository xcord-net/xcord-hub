namespace XcordHub.Infrastructure.Options;

public sealed class RedisOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ChannelPrefix { get; set; } = string.Empty;
}
