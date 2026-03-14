using System.ComponentModel.DataAnnotations;

namespace XcordHub.Infrastructure.Options;

public sealed class HubEmailOptions : EmailOptions
{
    /// <summary>
    /// The base URL of the hub frontend, used to construct password reset links.
    /// Example: https://hub.xcord.com
    /// </summary>
    [Required]
    public string HubBaseUrl { get; set; } = string.Empty;
}
