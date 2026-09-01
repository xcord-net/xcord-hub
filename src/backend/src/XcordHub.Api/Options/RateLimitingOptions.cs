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

    /// <summary>Max contact form submissions per minute per IP (default 3).</summary>
    public int ContactFormPermitLimit { get; set; } = 3;

    /// <summary>Max captcha issuances per minute per IP (default 20). Limits GIF harvesting.</summary>
    public int CaptchaPermitLimit { get; set; } = 20;

    /// <summary>
    /// Max federation bootstrap-token registration attempts per 15-minute window per IP.
    /// Defaults to 5 to slow brute-force token guessing on /api/v1/federation/register.
    /// </summary>
    public int BootstrapTokenPermitLimit { get; set; } = 5;

    /// <summary>Window (in minutes) for the bootstrap-token rate limiter (default 15).</summary>
    public int BootstrapTokenWindowMinutes { get; set; } = 15;

    /// <summary>
    /// Max /api/v1/admin/stats requests per minute per IP (default 30). The spark
    /// console polls twice a minute per open tab; the rest of the budget is
    /// headroom for a reload. This route is reachable from the public internet
    /// through the guest's catch-all proxy rule, so the limit is what stops the
    /// bearer token being brute-forced for free.
    /// </summary>
    public int AdminStatsPermitLimit { get; set; } = 30;
}
