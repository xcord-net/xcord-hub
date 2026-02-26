using System.Text.RegularExpressions;

namespace XcordHub;

public static partial class ValidationHelpers
{
    public static bool IsValidEmail(string email)
        => EmailRegex().IsMatch(email);

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

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"[A-Z]")]
    private static partial Regex UppercaseRegex();

    [GeneratedRegex(@"[a-z]")]
    private static partial Regex LowercaseRegex();

    [GeneratedRegex(@"[0-9]")]
    private static partial Regex DigitRegex();
}
