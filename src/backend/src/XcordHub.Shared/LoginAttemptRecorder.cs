using Microsoft.AspNetCore.Http;
using XcordHub.Entities;

namespace XcordHub;

public static class LoginAttemptRecorder
{
    public static LoginAttempt Create(
        SnowflakeId snowflakeGenerator,
        IHttpContextAccessor httpContextAccessor,
        string email,
        string? failureReason = null,
        long? userId = null)
    {
        var httpContext = httpContextAccessor.HttpContext;
        return new LoginAttempt
        {
            Id = snowflakeGenerator.NextId(),
            Email = email,
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent = httpContext?.Request.Headers.UserAgent.ToString() ?? "",
            Success = failureReason == null,
            FailureReason = failureReason,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
