using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Contact;

public sealed record SubmitContactFormRequest(string Name, string Email, string Company, int? ExpectedMemberCount, string Message);

public sealed record SubmitContactFormResponse(string Message);

public sealed class SubmitContactFormHandler(
    HubDbContext dbContext,
    SnowflakeIdGenerator snowflakeGenerator,
    IEmailService emailService)
    : IRequestHandler<SubmitContactFormRequest, Result<SubmitContactFormResponse>>, IValidatable<SubmitContactFormRequest>
{
    public Error? Validate(SubmitContactFormRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Error.Validation("VALIDATION_FAILED", "Name is required.");

        if (request.Name.Length > 100)
            return Error.Validation("VALIDATION_FAILED", "Name must not exceed 100 characters.");

        if (string.IsNullOrWhiteSpace(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Email is required.");

        if (request.Email.Length > 255)
            return Error.Validation("VALIDATION_FAILED", "Email must not exceed 255 characters.");

        if (!ValidationHelpers.IsValidEmail(request.Email))
            return Error.Validation("VALIDATION_FAILED", "Invalid email format.");

        if (!string.IsNullOrEmpty(request.Company) && request.Company.Length > 200)
            return Error.Validation("VALIDATION_FAILED", "Company must not exceed 200 characters.");

        if (string.IsNullOrWhiteSpace(request.Message))
            return Error.Validation("VALIDATION_FAILED", "Message is required.");

        if (request.Message.Length > 2000)
            return Error.Validation("VALIDATION_FAILED", "Message must not exceed 2000 characters.");

        if (request.ExpectedMemberCount.HasValue && request.ExpectedMemberCount.Value <= 0)
            return Error.Validation("VALIDATION_FAILED", "Expected member count must be greater than 0.");

        return null;
    }

    public async Task<Result<SubmitContactFormResponse>> Handle(SubmitContactFormRequest request, CancellationToken cancellationToken)
    {
        var submission = new ContactSubmission
        {
            Id = snowflakeGenerator.NextId(),
            Name = request.Name.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Company = request.Company?.Trim() ?? string.Empty,
            ExpectedMemberCount = request.ExpectedMemberCount,
            Message = request.Message.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ContactSubmissions.Add(submission);
        await dbContext.SaveChangesAsync(cancellationToken);

        var emailBody = BuildNotificationEmailBody(submission);
        await emailService.SendAsync("sales@xcord.net", "New Enterprise Contact Form Submission", emailBody);

        return new SubmitContactFormResponse("Your message has been submitted. Our team will be in touch shortly.");
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPost("/api/v1/hub/contact", async (
            SubmitContactFormRequest request,
            SubmitContactFormHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(request, ct);
        })
        .AllowAnonymous()
        .RequireRateLimiting("contact-form")
        .Produces<SubmitContactFormResponse>(200)
        .WithName("SubmitContactForm")
        .WithTags("Contact");
    }

    private static string BuildNotificationEmailBody(ContactSubmission submission)
    {
        var memberCountHtml = submission.ExpectedMemberCount.HasValue
            ? $"""
                        <tr>
                          <td style="color: #b5bac1; font-size: 14px; padding: 8px 0; vertical-align: top; width: 160px;">Expected Members</td>
                          <td style="color: #dbdee1; font-size: 14px; padding: 8px 0;">{submission.ExpectedMemberCount.Value:N0}</td>
                        </tr>
              """
            : string.Empty;

        var companyHtml = !string.IsNullOrEmpty(submission.Company)
            ? $"""
                        <tr>
                          <td style="color: #b5bac1; font-size: 14px; padding: 8px 0; vertical-align: top; width: 160px;">Company</td>
                          <td style="color: #dbdee1; font-size: 14px; padding: 8px 0;">{System.Web.HttpUtility.HtmlEncode(submission.Company)}</td>
                        </tr>
              """
            : string.Empty;

        return $"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background-color: #313338; margin: 0; padding: 40px 0;">
              <table width="100%" cellpadding="0" cellspacing="0" style="max-width: 480px; margin: 0 auto;">
                <tr>
                  <td style="background-color: #2b2d31; border-radius: 8px; padding: 40px;">
                    <h1 style="color: #ffffff; font-size: 22px; margin: 0 0 8px 0;">xcord</h1>
                    <h2 style="color: #dbdee1; font-size: 18px; margin: 0 0 24px 0;">New Enterprise Contact Submission</h2>
                    <table width="100%" cellpadding="0" cellspacing="0">
                      <tr>
                        <td style="color: #b5bac1; font-size: 14px; padding: 8px 0; vertical-align: top; width: 160px;">Name</td>
                        <td style="color: #dbdee1; font-size: 14px; padding: 8px 0;">{System.Web.HttpUtility.HtmlEncode(submission.Name)}</td>
                      </tr>
                      <tr>
                        <td style="color: #b5bac1; font-size: 14px; padding: 8px 0; vertical-align: top; width: 160px;">Email</td>
                        <td style="color: #dbdee1; font-size: 14px; padding: 8px 0;"><a href="mailto:{System.Web.HttpUtility.HtmlEncode(submission.Email)}" style="color: #d4943a; text-decoration: none;">{System.Web.HttpUtility.HtmlEncode(submission.Email)}</a></td>
                      </tr>
                      {companyHtml}
                      {memberCountHtml}
                      <tr>
                        <td style="color: #b5bac1; font-size: 14px; padding: 8px 0; vertical-align: top; width: 160px;">Message</td>
                        <td style="color: #dbdee1; font-size: 14px; padding: 8px 0; line-height: 1.6;">{System.Web.HttpUtility.HtmlEncode(submission.Message)}</td>
                      </tr>
                    </table>
                    <hr style="border: none; border-top: 1px solid #3f4147; margin: 24px 0;" />
                    <p style="color: #6d6f78; font-size: 11px; margin: 0;">
                      &copy; Xcord. All rights reserved.
                    </p>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
