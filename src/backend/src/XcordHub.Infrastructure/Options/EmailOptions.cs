using System.ComponentModel.DataAnnotations;

namespace XcordHub.Infrastructure.Options;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string SmtpHost { get; set; } = string.Empty;

    [Required]
    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 587;

    public string SmtpUsername { get; set; } = string.Empty;

    public string SmtpPassword { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string FromAddress { get; set; } = string.Empty;

    [Required]
    public string FromName { get; set; } = "Xcord";

    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// When true, emails are logged instead of sent — for development/test environments.
    /// </summary>
    [Required]
    public bool DevMode { get; set; }

    /// <summary>
    /// The base URL of the hub frontend, used to construct password reset links.
    /// Example: https://hub.xcord.com
    /// </summary>
    [Required]
    public string HubBaseUrl { get; set; } = string.Empty;
}
