using FluentAssertions;
using NetArchTest.Rules;

namespace XcordHub.Tests.Architecture;

public sealed class DependencyTests
{
    private const string SharedNamespace = "XcordHub";
    private const string FeaturesNamespace = "XcordHub.Features";
    private const string InfrastructureNamespace = "XcordHub.Infrastructure";
    private const string ApiNamespace = "XcordHub.Api";

    [Fact]
    public void Shared_ShouldNotDependOnOtherLayers()
    {
        // Arrange
        var assembly = typeof(XcordHub.Entities.HubUser).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(FeaturesNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Features_ShouldNotDependOnApi()
    {
        // Arrange
        var assembly = typeof(XcordHub.Features.FeaturesAssemblyMarker).Assembly;

        // Act
        // Note: Features CAN depend on Infrastructure (for data access and services)
        // but should NOT depend on Api (the host layer)
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }


    [Fact]
    public void Infrastructure_ShouldNotDependOnApi()
    {
        // Arrange
        var assembly = typeof(XcordHub.Infrastructure.Data.HubDbContext).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOnFeatures()
    {
        // Arrange
        var assembly = typeof(XcordHub.Infrastructure.Data.HubDbContext).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOn(FeaturesNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }
}
