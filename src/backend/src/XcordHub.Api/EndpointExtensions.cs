using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace XcordHub.Api;

public static class EndpointExtensions
{
    public static void MapHandlerEndpoints(this IEndpointRouteBuilder app, Assembly assembly)
    {
        var endpointTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsAssignableTo(typeof(IEndpoint)));

        foreach (var type in endpointTypes)
        {
            var builder = (RouteHandlerBuilder)type.GetMethod(nameof(IEndpoint.Map),
                BindingFlags.Public | BindingFlags.Static, null, [typeof(IEndpointRouteBuilder)], null)!
                .Invoke(null, [app])!;

            var autoName = type.Name.EndsWith("Handler") ? type.Name[..^7] : type.Name;
            builder.Finally(b =>
            {
                if (!b.Metadata.OfType<IEndpointNameMetadata>().Any())
                    b.Metadata.Add(new EndpointNameMetadata(autoName));
            });
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
