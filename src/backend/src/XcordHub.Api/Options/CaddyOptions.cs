namespace XcordHub.Api.Options;

public sealed class CaddyOptions
{
    public string AdminUrl { get; set; } = string.Empty;
    public bool UseReal { get; set; } = false;
}
