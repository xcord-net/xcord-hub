using FluentAssertions;
using XcordHub;

namespace XcordHub.Tests.Unit;

public sealed class ResultTests
{
    [Fact]
    public void Success_ShouldCreateSuccessfulResult()
    {
        // Act
        var result = Result<int>.Success(42);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_ShouldCreateFailedResult()
    {
        // Arrange
        var error = Error.NotFound("USER_NOT_FOUND", "User not found");

        // Act
        var result = Result<int>.Failure(error);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
        var act = () => result.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Success_ValueShouldBeAccessible()
    {
        // Arrange
        var result = Result<string>.Success("test value");

        // Act & Assert
        result.Value.Should().Be("test value");
    }

    [Fact]
    public void Failure_ErrorShouldBeAccessible()
    {
        // Arrange
        var error = Error.Validation("INVALID_INPUT", "Invalid input");
        var result = Result<string>.Failure(error);

        // Act & Assert
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Match_OnSuccessResult_ShouldInvokeSuccessFunction()
    {
        // Arrange
        var result = Result<int>.Success(42);

        // Act
        var output = result.Match(
            success: value => $"Success: {value}",
            failure: error => $"Error: {error.Message}");

        // Assert
        output.Should().Be("Success: 42");
    }

    [Fact]
    public void Match_OnFailureResult_ShouldInvokeFailureFunction()
    {
        // Arrange
        var error = Error.NotFound("NOT_FOUND", "Not found");
        var result = Result<int>.Failure(error);

        // Act
        var output = result.Match(
            success: value => $"Success: {value}",
            failure: error => $"Error: {error.Message}");

        // Assert
        output.Should().Be("Error: Not found");
    }

    [Fact]
    public void ImplicitConversion_FromValue_ShouldCreateSuccessResult()
    {
        // Act
        Result<string> result = "test value";

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("test value");
    }

    [Fact]
    public void ImplicitConversion_FromError_ShouldCreateFailureResult()
    {
        // Arrange
        var error = Error.Validation("INVALID_DATA", "Invalid data");

        // Act
        Result<string> result = error;

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void ImplicitConversion_InMethodReturn_ShouldWork()
    {
        // Arrange
        static Result<int> GetSuccessResult() => 100;
        static Result<int> GetFailureResult() => Error.NotFound("NOT_FOUND", "Not found");

        // Act
        var successResult = GetSuccessResult();
        var failureResult = GetFailureResult();

        // Assert
        successResult.IsSuccess.Should().BeTrue();
        successResult.Value.Should().Be(100);
        failureResult.IsFailure.Should().BeTrue();
        failureResult.Error!.Message.Should().Be("Not found");
    }

    [Fact]
    public void Success_WithNullValue_ShouldStillBeSuccess()
    {
        // Act
        var result = Result<string?>.Success(null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Failure_WithComplexError_ShouldPreserveErrorDetails()
    {
        // Arrange
        var error = new Error("CUSTOM_ERROR", "Custom error message", 418);

        // Act
        var result = Result<string>.Failure(error);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
        result.Error!.Code.Should().Be("CUSTOM_ERROR");
        result.Error.Message.Should().Be("Custom error message");
        result.Error.StatusCode.Should().Be(418);
    }
}
