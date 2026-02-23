using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace XcordHub;

public interface IEndpoint
{
    static abstract RouteHandlerBuilder Map(IEndpointRouteBuilder app);
}

public interface IRequestHandler<in TRequest, TResponse> : IEndpoint
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface IValidatable<in TRequest>
{
    Error? Validate(TRequest request);
}
