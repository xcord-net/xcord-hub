namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Service for sending emails via SMTP.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email to the specified recipient.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject.</param>
    /// <param name="htmlBody">HTML email body.</param>
    Task SendAsync(string to, string subject, string htmlBody);
}
