using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace XcordHub.Api;

public static class EndpointExtensions
{
    public static RouteHandlerBuilder MapPost<THandler>(this IEndpointRouteBuilder app, string pattern)
        where THandler : class
    {
        var (requestType, responseType) = ResolveHandlerTypes(typeof(THandler));
        return (RouteHandlerBuilder)typeof(EndpointExtensions)
            .GetMethod(nameof(MapPostTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(THandler), requestType, responseType)
            .Invoke(null, [app, pattern])!;
    }

    public static RouteHandlerBuilder MapGet<THandler>(this IEndpointRouteBuilder app, string pattern)
        where THandler : class
    {
        var (requestType, responseType) = ResolveHandlerTypes(typeof(THandler));
        return (RouteHandlerBuilder)typeof(EndpointExtensions)
            .GetMethod(nameof(MapGetTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(THandler), requestType, responseType)
            .Invoke(null, [app, pattern])!;
    }

    public static RouteHandlerBuilder MapPut<THandler>(this IEndpointRouteBuilder app, string pattern)
        where THandler : class
    {
        var (requestType, responseType) = ResolveHandlerTypes(typeof(THandler));
        return (RouteHandlerBuilder)typeof(EndpointExtensions)
            .GetMethod(nameof(MapPutTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(THandler), requestType, responseType)
            .Invoke(null, [app, pattern])!;
    }

    public static RouteHandlerBuilder MapPatch<THandler>(this IEndpointRouteBuilder app, string pattern)
        where THandler : class
    {
        var (requestType, responseType) = ResolveHandlerTypes(typeof(THandler));
        return (RouteHandlerBuilder)typeof(EndpointExtensions)
            .GetMethod(nameof(MapPatchTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(THandler), requestType, responseType)
            .Invoke(null, [app, pattern])!;
    }

    public static RouteHandlerBuilder MapDelete<THandler>(this IEndpointRouteBuilder app, string pattern)
        where THandler : class
    {
        var (requestType, responseType) = ResolveHandlerTypes(typeof(THandler));
        return (RouteHandlerBuilder)typeof(EndpointExtensions)
            .GetMethod(nameof(MapDeleteTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(THandler), requestType, responseType)
            .Invoke(null, [app, pattern])!;
    }

    private static RouteHandlerBuilder MapPostTyped<THandler, TRequest, TResponse>(
        IEndpointRouteBuilder app, string pattern)
        where THandler : class, IRequestHandler<TRequest, Result<TResponse>>
        where TRequest : class
    {
        return app.MapPost(pattern, async (
            [FromBody] TRequest request, [FromServices] THandler handler, CancellationToken ct)
            => await handler.ExecuteAsync(request, ct));
    }

    private static RouteHandlerBuilder MapGetTyped<THandler, TRequest, TResponse>(
        IEndpointRouteBuilder app, string pattern)
        where THandler : class, IRequestHandler<TRequest, Result<TResponse>>
        where TRequest : class
    {
        return app.MapGet(pattern, async (
            [AsParameters] TRequest request, [FromServices] THandler handler, CancellationToken ct)
            => await handler.ExecuteAsync(request, ct));
    }

    private static RouteHandlerBuilder MapPutTyped<THandler, TRequest, TResponse>(
        IEndpointRouteBuilder app, string pattern)
        where THandler : class, IRequestHandler<TRequest, Result<TResponse>>
        where TRequest : class
    {
        return app.MapPut(pattern, async (
            [FromBody] TRequest request, [FromServices] THandler handler, CancellationToken ct)
            => await handler.ExecuteAsync(request, ct));
    }

    private static RouteHandlerBuilder MapPatchTyped<THandler, TRequest, TResponse>(
        IEndpointRouteBuilder app, string pattern)
        where THandler : class, IRequestHandler<TRequest, Result<TResponse>>
        where TRequest : class
    {
        return app.MapPatch(pattern, async (
            [FromBody] TRequest request, [FromServices] THandler handler, CancellationToken ct)
            => await handler.ExecuteAsync(request, ct));
    }

    private static RouteHandlerBuilder MapDeleteTyped<THandler, TRequest, TResponse>(
        IEndpointRouteBuilder app, string pattern)
        where THandler : class, IRequestHandler<TRequest, Result<TResponse>>
        where TRequest : class
    {
        return app.MapDelete(pattern, async (
            [AsParameters] TRequest request, [FromServices] THandler handler, CancellationToken ct)
            => await handler.ExecuteAsync(request, ct));
    }

    private static (Type requestType, Type responseType) ResolveHandlerTypes(Type handlerType)
    {
        var handlerInterface = handlerType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
            ?? throw new InvalidOperationException(
                $"{handlerType.Name} does not implement IRequestHandler<TRequest, TResponse>");

        var args = handlerInterface.GetGenericArguments();
        var resultType = args[1];

        if (!resultType.IsGenericType || resultType.GetGenericTypeDefinition() != typeof(Result<>))
            throw new InvalidOperationException(
                $"{handlerType.Name} TResponse must be Result<T>, got {resultType.Name}");

        return (args[0], resultType.GetGenericArguments()[0]);
    }

    public static void MapHandlerEndpoints(this IEndpointRouteBuilder app, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetMethod("Map", BindingFlags.Public | BindingFlags.Static,
                null, [typeof(IEndpointRouteBuilder)], null) is not null);

        foreach (var type in handlerTypes)
        {
            type.GetMethod("Map", BindingFlags.Public | BindingFlags.Static,
                null, [typeof(IEndpointRouteBuilder)], null)!
                .Invoke(null, [app]);
        }
    }

    public static IServiceCollection AddRequestHandlers(this IServiceCollection services, Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                        t.GetInterfaces().Any(i =>
                            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)));

        foreach (var type in handlerTypes)
        {
            services.AddScoped(type);
            // Also register by interface so minimal API can resolve handler parameters
            var handlerInterface = type.GetInterfaces()
                .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));
            services.AddScoped(handlerInterface, type);
        }

        return services;
    }
}
