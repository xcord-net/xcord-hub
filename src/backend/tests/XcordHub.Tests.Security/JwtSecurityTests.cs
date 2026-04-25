using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Options;
using XcordHub.Infrastructure.Services;
using Xunit;

namespace XcordHub.Tests.Security;

/// <summary>
/// Per-class Postgres container fixture so JwtSecurityTests can persist RSA keys
/// in a real database without depending on SharedPostgresFixture (which lives in
/// XcordHub.Tests.Infrastructure and uses an assembly-scoped collection).
/// </summary>
public sealed class JwtSecurityFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_jwt_security")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition("JwtSecurity")]
public class JwtSecurityCollection : ICollectionFixture<JwtSecurityFixture> { }

[Trait("Category", "Security")]
[Collection("JwtSecurity")]
public sealed class JwtSecurityTests
{
    private const string ValidIssuer = "xcord-hub-test";
    private const string ValidAudience = "xcord-hub-users";
    private const string TestEncryptionKey = "jwt-security-tests-encryption-key-256-bits-minimum-length-ok!";

    private readonly JwtSecurityFixture _fixture;

    public JwtSecurityTests(JwtSecurityFixture fixture)
    {
        _fixture = fixture;
    }

    private HubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
        var ctx = new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private (IJwtService jwt, RsaSecurityKey publicKey, HubDbContext db) CreateJwtService(
        string issuer = ValidIssuer,
        string audience = ValidAudience,
        int expirationMinutes = 15)
    {
        var db = CreateDbContext();
        var encryption = new AesEncryptionService(TestEncryptionKey);
        var rsaKeySingleton = new RsaKeySingleton();
        var options = Options.Create(new JwtOptions
        {
            Issuer = issuer,
            Audience = audience,
            AccessTokenExpirationMinutes = expirationMinutes
        });

        var jwt = new JwtService(
            db,
            options,
            rsaKeySingleton,
            encryption,
            NullLogger<JwtService>.Instance);

        jwt.EnsureRsaKeyPairAsync().GetAwaiter().GetResult();
        var publicKeyEntry = db.SystemSettings.First(s => s.Key == JwtService.RsaPublicKeySettingKey);
        rsaKeySingleton.LoadPublicKey(publicKeyEntry.Value);

        return (jwt, rsaKeySingleton.GetPublicKey(), db);
    }

    [Fact]
    public void ValidToken_ShouldBeAccepted()
    {
        var (jwt, publicKey, db) = CreateJwtService();
        try
        {
            var token = jwt.GenerateAccessToken(12345, false);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = ValidIssuer,
                ValidAudience = ValidAudience,
                IssuerSigningKey = publicKey,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                ClockSkew = TimeSpan.Zero
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            validatedToken.Should().NotBeNull();
            principal.Should().NotBeNull();
            principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be("12345");
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public void TokenWithWrongSigningKey_ShouldBeRejected()
    {
        var (jwt, _, db) = CreateJwtService();
        try
        {
            var token = jwt.GenerateAccessToken(12345, false);

            using var unrelated = System.Security.Cryptography.RSA.Create(3072);
            var wrongKey = new RsaSecurityKey(unrelated);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = ValidIssuer,
                ValidAudience = ValidAudience,
                IssuerSigningKey = wrongKey,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                ClockSkew = TimeSpan.Zero
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var act = () => tokenHandler.ValidateToken(token, validationParameters, out _);
            act.Should().Throw<SecurityTokenInvalidSignatureException>();
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public void ExpiredToken_ShouldBeRejected()
    {
        var (jwt, publicKey, db) = CreateJwtService(expirationMinutes: -1);
        try
        {
            var token = jwt.GenerateAccessToken(12345, false);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = ValidIssuer,
                ValidAudience = ValidAudience,
                IssuerSigningKey = publicKey,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                ClockSkew = TimeSpan.Zero
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var act = () => tokenHandler.ValidateToken(token, validationParameters, out _);
            act.Should().Throw<SecurityTokenExpiredException>();
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public void TokenWithWrongIssuer_ShouldBeRejected()
    {
        var (jwt, publicKey, db) = CreateJwtService(issuer: "wrong-issuer");
        try
        {
            var token = jwt.GenerateAccessToken(12345, false);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = ValidIssuer,
                ValidAudience = ValidAudience,
                IssuerSigningKey = publicKey,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                ClockSkew = TimeSpan.Zero
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var act = () => tokenHandler.ValidateToken(token, validationParameters, out _);
            act.Should().Throw<SecurityTokenInvalidIssuerException>();
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public void TokenWithWrongAudience_ShouldBeRejected()
    {
        var (jwt, publicKey, db) = CreateJwtService(audience: "wrong-audience");
        try
        {
            var token = jwt.GenerateAccessToken(12345, false);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = ValidIssuer,
                ValidAudience = ValidAudience,
                IssuerSigningKey = publicKey,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                ClockSkew = TimeSpan.Zero
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var act = () => tokenHandler.ValidateToken(token, validationParameters, out _);
            act.Should().Throw<SecurityTokenInvalidAudienceException>();
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public void AdminClaim_ShouldBePresent()
    {
        var (jwt, publicKey, db) = CreateJwtService();
        try
        {
            var token = jwt.GenerateAccessToken(12345, true);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = ValidIssuer,
                ValidAudience = ValidAudience,
                IssuerSigningKey = publicKey,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                ClockSkew = TimeSpan.Zero
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);

            principal.FindFirst("admin")?.Value.Should().Be("true");
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public void NonAdminToken_ShouldHaveAdminFalse()
    {
        var (jwt, publicKey, db) = CreateJwtService();
        try
        {
            var token = jwt.GenerateAccessToken(12345, false);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = ValidIssuer,
                ValidAudience = ValidAudience,
                IssuerSigningKey = publicKey,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                ClockSkew = TimeSpan.Zero
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);

            principal.FindFirst("admin")?.Value.Should().Be("false");
        }
        finally
        {
            db.Dispose();
        }
    }

    [Fact]
    public void GeneratedToken_UsesRs256Algorithm()
    {
        var (jwt, _, db) = CreateJwtService();
        try
        {
            var token = jwt.GenerateAccessToken(12345, false);
            var jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(token);
            jwtToken.SignatureAlgorithm.Should().Be(SecurityAlgorithms.RsaSha256);
        }
        finally
        {
            db.Dispose();
        }
    }
}
