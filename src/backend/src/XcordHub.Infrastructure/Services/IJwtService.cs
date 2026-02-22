namespace XcordHub.Infrastructure.Services;

public interface IJwtService
{
    string GenerateAccessToken(long userId, bool isAdmin);
}
