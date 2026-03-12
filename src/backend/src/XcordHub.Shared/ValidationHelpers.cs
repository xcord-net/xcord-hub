using System.Text.RegularExpressions;

namespace XcordHub;

public static partial class ValidationHelpers
{
    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "mail", "smtp", "imap", "pop", "ftp",
        "docker", "registry",
        "api", "admin", "hub", "auth",
        "ns1", "ns2", "ns3", "ns4",
        "caddy", "proxy", "lb",
        "pg", "postgres", "redis", "minio", "s3",
        "livekit", "rtc", "turn", "stun",
        "status", "monitor", "grafana", "prometheus",
        "_dmarc", "autoconfig", "autodiscover",
    };

    public static bool IsValidEmail(string email)
        => EmailRegex().IsMatch(email);

    /// <summary>
    /// Validates a subdomain label (e.g. "myserver" in "myserver.xcord.net").
    /// Rules: 6-63 chars, lowercase alphanumeric + hyphens, no leading/trailing/consecutive hyphens, not reserved.
    /// </summary>
    public static Error? ValidateSubdomain(string? subdomain)
    {
        if (string.IsNullOrWhiteSpace(subdomain))
            return Error.Validation("VALIDATION_FAILED", "Subdomain is required");

        if (subdomain.Length < 6 || subdomain.Length > 63)
            return Error.Validation("VALIDATION_FAILED", "Subdomain must be 6-63 characters");

        if (!SubdomainRegex().IsMatch(subdomain))
            return Error.Validation("VALIDATION_FAILED", "Subdomain must contain only lowercase letters, numbers, and hyphens (not at start or end)");

        if (subdomain.Contains("--"))
            return Error.Validation("VALIDATION_FAILED", "Subdomain must not contain consecutive hyphens");

        if (ReservedSubdomains.Contains(subdomain))
            return Error.Validation("RESERVED_SUBDOMAIN", $"'{subdomain}' is reserved for infrastructure use");

        return null;
    }

    /// <summary>
    /// Validates a full domain (e.g. "myserver.xcord.net").
    /// Each label must be a valid subdomain label; the first label must not be reserved.
    /// </summary>
    public static Error? ValidateDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return Error.Validation("VALIDATION_FAILED", "Domain is required");

        if (domain.Length > 253)
            return Error.Validation("VALIDATION_FAILED", "Domain must not exceed 253 characters");

        var labels = domain.Split('.');
        if (labels.Length < 2)
            return Error.Validation("VALIDATION_FAILED", "Domain must contain at least two labels");

        foreach (var label in labels)
        {
            if (string.IsNullOrEmpty(label) || label.Length > 63)
                return Error.Validation("VALIDATION_FAILED", "Each domain label must be 1-63 characters");

            if (!SubdomainRegex().IsMatch(label))
                return Error.Validation("VALIDATION_FAILED", $"Domain label '{label}' contains invalid characters");

            if (label.Contains("--"))
                return Error.Validation("VALIDATION_FAILED", $"Domain label '{label}' must not contain consecutive hyphens");
        }

        // First label (subdomain) must not be reserved
        if (ReservedSubdomains.Contains(labels[0]))
            return Error.Validation("RESERVED_SUBDOMAIN", $"'{labels[0]}' is reserved for infrastructure use");

        return null;
    }

    public static bool IsReservedSubdomain(string subdomain)
        => ReservedSubdomains.Contains(subdomain);

    public static Error? ValidatePasswordComplexity(string password)
    {
        if (password.Length < 8 || password.Length > 128)
            return Error.Validation("VALIDATION_FAILED", "Password must be between 8 and 128 characters");

        if (!UppercaseRegex().IsMatch(password))
            return Error.Validation("VALIDATION_FAILED", "Password must contain at least one uppercase letter");

        if (!LowercaseRegex().IsMatch(password))
            return Error.Validation("VALIDATION_FAILED", "Password must contain at least one lowercase letter");

        if (!DigitRegex().IsMatch(password))
            return Error.Validation("VALIDATION_FAILED", "Password must contain at least one number");

        return null;
    }

    public static string ExtractSubdomain(string domain)
        => domain.Split('.')[0];

    // Matches a single valid DNS label: starts/ends with alphanumeric, hyphens allowed in middle
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SubdomainRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"[A-Z]")]
    private static partial Regex UppercaseRegex();

    [GeneratedRegex(@"[a-z]")]
    private static partial Regex LowercaseRegex();

    [GeneratedRegex(@"[0-9]")]
    private static partial Regex DigitRegex();
}
