using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using XcordHub.Infrastructure.Options;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// SMTP implementation of IEmailService using MailKit.
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(
        IOptions<EmailOptions> options,
        ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        // DevMode: log email instead of sending
        if (_options.DevMode)
        {
            _logger.LogInformation(
                "DevMode Email: To={To}, Subject={Subject}, Body={BodyPreview}",
                to,
                subject,
                htmlBody.Length > 100 ? htmlBody.Substring(0, 100) + "..." : htmlBody);
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
            message.To.Add(new MailboxAddress("", to));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();

            // Connect to SMTP server
            await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, _options.UseSsl);

            // Authenticate if credentials are provided
            if (!string.IsNullOrWhiteSpace(_options.SmtpUsername) &&
                !string.IsNullOrWhiteSpace(_options.SmtpPassword))
            {
                await client.AuthenticateAsync(_options.SmtpUsername, _options.SmtpPassword);
            }

            // Send the message
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent successfully to {To}", to);
        }
        catch (Exception ex)
        {
            // Log but don't throw — emails are best-effort
            _logger.LogError(ex, "Failed to send email to {To}", to);
        }
    }
}
