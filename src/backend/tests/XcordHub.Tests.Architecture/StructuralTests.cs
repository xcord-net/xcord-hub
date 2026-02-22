using FluentAssertions;
using NetArchTest.Rules;
using System.Reflection;

namespace XcordHub.Tests.Architecture;

public sealed class StructuralTests
{
    private const string EntitiesNamespace = "XcordHub.Entities";

    [Fact]
    public void Entities_ShouldHaveIdPropertyOfTypeLong()
    {
        // Arrange
        var assembly = typeof(XcordHub.Entities.HubUser).Assembly;

        // Act
        var entityTypes = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(EntitiesNamespace)
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .GetTypes()
            .Where(t => !t.IsEnum);

        // Assert
        foreach (var entityType in entityTypes)
        {
            var idProperty = entityType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);

            // Some entities may be join tables without Id - they use composite keys
            if (idProperty == null)
                continue;

            idProperty.PropertyType.Should().Be(typeof(long), $"{entityType.Name}.Id should be of type long");
        }
    }

    [Fact]
    public void Entities_ShouldBeSealed()
    {
        // Arrange
        var assembly = typeof(XcordHub.Entities.HubUser).Assembly;

        // Act
        var entityTypes = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(EntitiesNamespace)
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .GetTypes()
            .Where(t => !t.IsEnum);

        // Assert
        foreach (var entityType in entityTypes)
        {
            entityType.IsSealed.Should().BeTrue($"{entityType.Name} should be sealed");
        }
    }

    [Fact]
    public void Entities_ShouldHavePublicParameterlessConstructor()
    {
        // Arrange
        var assembly = typeof(XcordHub.Entities.HubUser).Assembly;

        // Act
        var entityTypes = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(EntitiesNamespace)
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .GetTypes()
            .Where(t => !t.IsEnum);

        // Assert
        foreach (var entityType in entityTypes)
        {
            var parameterlessConstructor = entityType.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);

            parameterlessConstructor.Should().NotBeNull(
                $"{entityType.Name} should have a public parameterless constructor for EF Core");
        }
    }

    [Fact]
    public void DomainEntities_ShouldNotDependOnInfrastructure()
    {
        // Arrange
        var assembly = typeof(XcordHub.Entities.HubUser).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(EntitiesNamespace)
            .ShouldNot()
            .HaveDependencyOn("XcordHub.Infrastructure")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("Domain entities should not depend on Infrastructure");
    }

    [Fact]
    public void DomainEntities_ShouldNotDependOnFeatures()
    {
        // Arrange
        var assembly = typeof(XcordHub.Entities.HubUser).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace(EntitiesNamespace)
            .ShouldNot()
            .HaveDependencyOn("XcordHub.Features")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue("Domain entities should not depend on Features");
    }

    [Fact]
    public void Enums_ShouldResideInEntitiesNamespace()
    {
        // Arrange
        var assembly = typeof(XcordHub.Entities.InstanceStatus).Assembly;

        // Act
        var enumTypes = assembly.GetTypes()
            .Where(t => t.IsEnum && t.Namespace != null && t.Namespace.StartsWith("XcordHub.Entities"))
            .ToList();

        // Assert - all enums should be in XcordHub.Entities namespace
        foreach (var enumType in enumTypes)
        {
            enumType.Namespace.Should().Be("XcordHub.Entities",
                $"{enumType.Name} should be in XcordHub.Entities namespace");
        }
    }
}
