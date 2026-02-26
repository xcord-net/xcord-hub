using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace XcordHub.Features.Auth;

public sealed record GetCaptchaQuery();

public sealed record CaptchaResponse(string CaptchaId, string Question);

public sealed class CaptchaHandler(ICaptchaService captchaService)
    : IRequestHandler<GetCaptchaQuery, Result<CaptchaResponse>>
{
    public async Task<Result<CaptchaResponse>> Handle(GetCaptchaQuery request, CancellationToken cancellationToken)
    {
        var challenge = await captchaService.GenerateAsync();
        return new CaptchaResponse(challenge.Id, challenge.Question);
    }

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app)
    {
        return app.MapGet("/api/v1/auth/captcha", async (
            CaptchaHandler handler,
            CancellationToken ct) =>
        {
            return await handler.ExecuteAsync(new GetCaptchaQuery(), ct);
        })
        .AllowAnonymous()
        .Produces<CaptchaResponse>(200)
        .WithName("GetCaptcha")
        .WithTags("Auth");
    }
}
