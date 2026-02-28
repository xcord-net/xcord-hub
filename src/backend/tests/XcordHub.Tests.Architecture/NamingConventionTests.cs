using FluentAssertions;
using NetArchTest.Rules;

namespace XcordHub.Tests.Architecture;

public sealed class NamingConventionTests
{
    private const string FeaturesNamespace = "XcordHub.Features";

    [Fact]
    public void Commands_ShouldEndWithCommand()
    {
        // Arrange
        var assembly = typeof(XcordHub.Features.FeaturesAssemblyMarker).Assembly;

        // Act
        var commandTypes = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(FeaturesNamespace)
            .And()
            .AreClasses()
            .And()
            .HaveNameMatching(".*Command$")
            .GetTypes();

        // Assert - guard against vacuous pass when no types match
        commandTypes.Should().NotBeEmpty("expected to find types matching the Command pattern");
        foreach (var type in commandTypes)
        {
            type.Name.Should().EndWith("Command");
        }
    }

    [Fact]
    public void Queries_ShouldEndWithQuery()
    {
        // Arrange
        var assembly = typeof(XcordHub.Features.FeaturesAssemblyMarker).Assembly;

        // Act
        var queryTypes = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(FeaturesNamespace)
            .And()
            .AreClasses()
            .And()
            .HaveNameMatching(".*Query$")
            .GetTypes();

        // Assert - guard against vacuous pass when no types match
        queryTypes.Should().NotBeEmpty("expected to find types matching the Query pattern");
        foreach (var type in queryTypes)
        {
            type.Name.Should().EndWith("Query");
        }
    }

    [Fact]
    public void EndpointImplementors_ShouldBeHandlers()
    {
        // Arrange — all IEndpoint implementors should follow the Handler naming convention
        var assembly = typeof(XcordHub.Features.FeaturesAssemblyMarker).Assembly;

        // Act
        var endpointTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsAssignableTo(typeof(IEndpoint)));

        // Assert
        endpointTypes.Should().NotBeEmpty("expected to find IEndpoint implementors");
        foreach (var type in endpointTypes)
        {
            type.Name.Should().EndWith("Handler", $"{type.Name} implements IEndpoint but doesn't follow Handler naming");
            type.IsSealed.Should().BeTrue($"{type.Name} implements IEndpoint and should be sealed");
        }
    }

    [Fact]
    public void Handlers_ShouldBeSealed()
    {
        // Arrange
        var assembly = typeof(XcordHub.Features.FeaturesAssemblyMarker).Assembly;

        // Act
        var handlerTypes = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(FeaturesNamespace)
            .And()
            .HaveNameEndingWith("Handler")
            .GetTypes();

        // Assert - all handlers should be sealed
        handlerTypes.Should().NotBeEmpty("expected to find types matching the Handler pattern");
        foreach (var type in handlerTypes)
        {
            type.IsSealed.Should().BeTrue($"{type.Name} should be sealed");
        }
    }

}
