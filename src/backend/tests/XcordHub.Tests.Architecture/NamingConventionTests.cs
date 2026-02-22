using FluentAssertions;
using NetArchTest.Rules;

namespace XcordHub.Tests.Architecture;

public sealed class NamingConventionTests
{
    private const string FeaturesNamespace = "XcordHub.Features";

    [Fact]
    public void Handlers_ShouldEndWithHandler()
    {
        // Arrange
        var assembly = typeof(XcordHub.Features.FeaturesAssemblyMarker).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(FeaturesNamespace)
            .And()
            .AreClasses()
            .And()
            .HaveNameMatching(".*Handler$")
            .Should()
            .HaveNameEndingWith("Handler")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }

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
    public void Endpoints_ShouldEndWithEndpoint()
    {
        // Arrange
        var assembly = typeof(XcordHub.Features.FeaturesAssemblyMarker).Assembly;

        // Act
        var endpointTypes = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(FeaturesNamespace)
            .And()
            .AreClasses()
            .And()
            .HaveNameMatching(".*Endpoint$")
            .GetTypes();

        // Assert - guard against vacuous pass when no types match
        endpointTypes.Should().NotBeEmpty("expected to find types matching the Endpoint pattern");
        foreach (var type in endpointTypes)
        {
            type.Name.Should().EndWith("Endpoint");
        }
    }

    [Fact]
    public void Handlers_ShouldNotBeAbstract()
    {
        // Arrange
        var assembly = typeof(XcordHub.Features.FeaturesAssemblyMarker).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(FeaturesNamespace)
            .And()
            .HaveNameEndingWith("Handler")
            .Should()
            .NotBeAbstract()
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Endpoints_ShouldBeSealed()
    {
        // Arrange
        var assembly = typeof(XcordHub.Features.FeaturesAssemblyMarker).Assembly;

        // Act
        var endpointTypes = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(FeaturesNamespace)
            .And()
            .HaveNameEndingWith("Endpoint")
            .GetTypes();

        // Assert - all endpoints should be sealed
        endpointTypes.Should().NotBeEmpty("expected to find types matching the Endpoint pattern");
        foreach (var type in endpointTypes)
        {
            type.IsSealed.Should().BeTrue($"{type.Name} should be sealed");
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
