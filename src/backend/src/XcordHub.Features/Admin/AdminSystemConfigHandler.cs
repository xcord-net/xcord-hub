using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using XcordHub.Features.Auth;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Features.Admin;

public sealed record AdminSystemConfigResponse(
    bool PaidServersDisabled,
    DateTimeOffset UpdatedAt
);

public sealed record UpdateAdminSystemConfigRequest(
    bool PaidServersDisabled
);

public sealed class AdminGetSystemConfigHandler : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/admin/system-config", async (
            ISystemConfigService service,
            CancellationToken ct) =>
        {
            var config = await service.GetAsync(ct);
            return Results.Ok(new AdminSystemConfigResponse(config.PaidServersDisabled, config.UpdatedAt));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<AdminSystemConfigResponse>(200)
        .WithName("AdminGetSystemConfig")
        .WithTags("Admin");
    }
}

public sealed class AdminUpdateSystemConfigHandler : IEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapPut("/api/v1/admin/system-config", async (
            UpdateAdminSystemConfigRequest request,
            ISystemConfigService service,
            CancellationToken ct) =>
        {
            var config = await service.SetPaidServersDisabledAsync(request.PaidServersDisabled, ct);
            return Results.Ok(new AdminSystemConfigResponse(config.PaidServersDisabled, config.UpdatedAt));
        })
        .RequireAuthorization(Policies.Admin)
        .Produces<AdminSystemConfigResponse>(200)
        .WithName("AdminUpdateSystemConfig")
        .WithTags("Admin");
    }
}
