namespace XcordHub.Api.Options;

public sealed class RateLimitingOptions
{
    public int TokenLimit { get; set; } = 100;
    public int ReplenishmentPeriodSeconds { get; set; } = 10;
    public int TokensPerPeriod { get; set; } = 20;

    /// <summary>Max registrations per minute per IP (default 3).</summary>
    public int AuthRegisterPermitLimit { get; set; } = 3;

    /// <summary>Max password-reset requests per minute per IP (default 3).</summary>
    public int AuthForgotPasswordPermitLimit { get; set; } = 3;
}
