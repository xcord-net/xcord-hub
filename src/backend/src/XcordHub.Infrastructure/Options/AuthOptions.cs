using System.ComponentModel.DataAnnotations;

namespace XcordHub.Infrastructure.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Range(4, 31)]
    public int BcryptWorkFactor { get; set; } = 12;

    /// <summary>
    /// JWT access-token lifetime in minutes.
    /// </summary>
    [Range(1, 1440)]
    public int JwtAccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Refresh-token lifetime in days. Drives both the database row's ExpiresAt
    /// and the refresh-token cookie's Max-Age.
    /// </summary>
    [Range(1, 365)]
    public int JwtRefreshTokenDays { get; set; } = 30;

    /// <summary>
    /// Maximum failed login attempts permitted within
    /// <see cref="LoginAttemptWindowMinutes"/> before lockout.
    /// </summary>
    [Range(1, 100)]
    public int MaxLoginAttemptsPerWindow { get; set; } = 5;

    /// <summary>
    /// Rolling window in minutes for failed login attempts. Also doubles as the
    /// lockout duration once the cap is hit.
    /// </summary>
    [Range(1, 1440)]
    public int LoginAttemptWindowMinutes { get; set; } = 15;
}
