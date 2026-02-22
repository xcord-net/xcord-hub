namespace XcordHub.Api.Options;

public sealed class RateLimitingOptions
{
    public int TokenLimit { get; set; } = 100;
    public int ReplenishmentPeriodSeconds { get; set; } = 10;
    public int TokensPerPeriod { get; set; } = 20;
}
