using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Security;

[Trait("Category", "Security")]
public sealed class JwtSecurityTests
{
    private const string ValidIssuer = "xcord-hub-test";
    private const string ValidAudience = "xcord-hub-users";
    private const string ValidSecretKey = "test-secret-key-with-minimum-256-bits-for-hmacsha256";
    private const string WrongSecretKey = "wrong-secret-key-with-minimum-256-bits-for-hmacsha256";

    [Fact]
    public void ValidToken_ShouldBeAccepted()
    {
        // Arrange
        var jwtService = new JwtService(ValidIssuer, ValidAudience, ValidSecretKey, 15);
        var token = jwtService.GenerateAccessToken(12345, false);

        // Act
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = ValidIssuer,
            ValidAudience = ValidAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSecretKey)),
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

        // Assert
        validatedToken.Should().NotBeNull();
        principal.Should().NotBeNull();
        principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be("12345");
    }

    [Fact]
    public void TokenWithWrongSigningKey_ShouldBeRejected()
    {
        // Arrange
        var jwtService = new JwtService(ValidIssuer, ValidAudience, ValidSecretKey, 15);
        var token = jwtService.GenerateAccessToken(12345, false);

        // Act
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = ValidIssuer,
            ValidAudience = ValidAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(WrongSecretKey)),
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();

        // Assert
        var act = () => tokenHandler.ValidateToken(token, validationParameters, out _);
        act.Should().Throw<SecurityTokenException>();
    }

    [Fact]
    public void ExpiredToken_ShouldBeRejected()
    {
        // Arrange - create token with -1 minute expiration (already expired)
        var jwtService = new JwtService(ValidIssuer, ValidAudience, ValidSecretKey, -1);
        var token = jwtService.GenerateAccessToken(12345, false);

        // Act
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = ValidIssuer,
            ValidAudience = ValidAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSecretKey)),
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();

        // Assert
        var act = () => tokenHandler.ValidateToken(token, validationParameters, out _);
        act.Should().Throw<SecurityTokenExpiredException>();
    }

    [Fact]
    public void TokenWithWrongIssuer_ShouldBeRejected()
    {
        // Arrange
        var jwtService = new JwtService("wrong-issuer", ValidAudience, ValidSecretKey, 15);
        var token = jwtService.GenerateAccessToken(12345, false);

        // Act
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = ValidIssuer,
            ValidAudience = ValidAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSecretKey)),
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();

        // Assert
        var act = () => tokenHandler.ValidateToken(token, validationParameters, out _);
        act.Should().Throw<SecurityTokenInvalidIssuerException>();
    }

    [Fact]
    public void TokenWithWrongAudience_ShouldBeRejected()
    {
        // Arrange
        var jwtService = new JwtService(ValidIssuer, "wrong-audience", ValidSecretKey, 15);
        var token = jwtService.GenerateAccessToken(12345, false);

        // Act
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = ValidIssuer,
            ValidAudience = ValidAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSecretKey)),
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();

        // Assert
        var act = () => tokenHandler.ValidateToken(token, validationParameters, out _);
        act.Should().Throw<SecurityTokenInvalidAudienceException>();
    }

    [Fact]
    public void AdminClaim_ShouldBePresent()
    {
        // Arrange
        var jwtService = new JwtService(ValidIssuer, ValidAudience, ValidSecretKey, 15);
        var token = jwtService.GenerateAccessToken(12345, true);

        // Act
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = ValidIssuer,
            ValidAudience = ValidAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSecretKey)),
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(token, validationParameters, out _);

        // Assert
        principal.FindFirst("admin")?.Value.Should().Be("true");
    }

    [Fact]
    public void NonAdminToken_ShouldHaveAdminFalse()
    {
        // Arrange
        var jwtService = new JwtService(ValidIssuer, ValidAudience, ValidSecretKey, 15);
        var token = jwtService.GenerateAccessToken(12345, false);

        // Act
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = ValidIssuer,
            ValidAudience = ValidAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ValidSecretKey)),
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(token, validationParameters, out _);

        // Assert
        principal.FindFirst("admin")?.Value.Should().Be("false");
    }
}
