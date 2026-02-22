namespace XcordHub.Infrastructure.Services;

public interface ICurrentUserService
{
    Result<long> GetCurrentUserId();
}
