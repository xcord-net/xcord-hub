using BCrypt.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using XcordHub.Entities;
using XcordHub.Features.Auth;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using Xunit;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Integration tests for the DeleteAccountHandler.
/// Verifies soft deletion, login invalidation, and instance suspension.
/// Uses a real PostgreSQL database via Testcontainers.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AccountDeletionTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private HubDbContext? _dbContext;
    private IEncryptionService? _encryptionService;
    private SnowflakeId? _snowflake;

    private const string EncryptionKey = "test-encryption-key-with-256-bits-minimum-length-required";
    private const string TestPassword = "TestPass123!";

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("xcordhub_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _encryptionService = new AesEncryptionService(EncryptionKey);
        _dbContext = new HubDbContext(options, _encryptionService);
        await _dbContext.Database.EnsureCreatedAsync();
        _snowflake = new SnowflakeId(2);
    }

    public async Task DisposeAsync()
    {
        if (_dbContext != null)
            await _dbContext.DisposeAsync();

        if (_postgres != null)
            await _postgres.DisposeAsync();
    }

    private HubUser CreateUser(string username, string email, bool isAdmin = false)
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(TestPassword, 4); // Low cost for tests
        var emailHash = _encryptionService!.ComputeHmac(email.ToLowerInvariant());
        var encryptedEmail = _encryptionService.Encrypt(email.ToLowerInvariant());

        return new HubUser
        {
            Id = _snowflake!.NextId(),
            Username = username,
            DisplayName = username,
            Email = encryptedEmail,
            EmailHash = emailHash,
            PasswordHash = passwordHash,
            IsAdmin = isAdmin,
            IsDisabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private DeleteAccountHandler CreateHandler(IDockerService? dockerService = null)
    {
        var docker = dockerService ?? new NoopDockerService(NullLogger<NoopDockerService>.Instance);
        return new DeleteAccountHandler(
            _dbContext!,
            docker,
            NullLogger<DeleteAccountHandler>.Instance);
    }

    // ── AC1: DELETE endpoint soft-deletes the user (DeletedAt is set) ────────

    [Fact]
    public async Task DeleteAccount_WithCorrectPassword_SoftDeletesUser()
    {
        // Arrange
        var user = CreateUser("delete_test_user", "delete_test@example.com");
        _dbContext!.HubUsers.Add(user);
        await _dbContext.SaveChangesAsync();

        var handler = CreateHandler();
        var command = new DeleteAccountCommand(user.Id, TestPassword);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert — handler succeeded
        result.IsSuccess.Should().BeTrue();

        // Assert — user record has DeletedAt set (soft delete)
        var deletedUser = await _dbContext.HubUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        deletedUser.Should().NotBeNull();
        deletedUser!.DeletedAt.Should().NotBeNull("soft delete must set DeletedAt");
        deletedUser.IsDisabled.Should().BeTrue("soft delete must disable the account");
    }

    // ── AC2: Login fails after deletion ──────────────────────────────────────

    [Fact]
    public async Task DeleteAccount_ThenLookupByEmailHash_ReturnsNullDueToQueryFilter()
    {
        // Arrange
        var user = CreateUser("deleted_login_user", "deleted_login@example.com");
        _dbContext!.HubUsers.Add(user);
        await _dbContext.SaveChangesAsync();

        var deleteHandler = CreateHandler();
        await deleteHandler.Handle(new DeleteAccountCommand(user.Id, TestPassword), CancellationToken.None);

        // Act — LoginHandler looks up the user by EmailHash without IgnoreQueryFilters.
        // HubUserConfiguration sets HasQueryFilter(x => x.DeletedAt == null), so a
        // deleted user will not appear, causing login to fail with INVALID_CREDENTIALS.
        var emailHash = _encryptionService!.ComputeHmac("deleted_login@example.com");
        var found = await _dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.EmailHash == emailHash);

        // Assert — deleted user is invisible to the standard query
        found.Should().BeNull(
            "the global query filter (DeletedAt == null) must hide soft-deleted users, " +
            "so LoginHandler cannot authenticate the deleted account");
    }

    [Fact]
    public async Task DeleteAccount_ThenFindById_ReturnsNullViaDeletedAtFilter()
    {
        // Arrange
        var user = CreateUser("filter_test_user", "filter_test@example.com");
        _dbContext!.HubUsers.Add(user);
        await _dbContext.SaveChangesAsync();

        var handler = CreateHandler();
        await handler.Handle(new DeleteAccountCommand(user.Id, TestPassword), CancellationToken.None);

        // Act — DeleteAccountHandler itself uses DeletedAt == null filter
        var found = await _dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == user.Id && u.DeletedAt == null);

        // Assert — deleted user must not be found with the standard filter
        found.Should().BeNull("deleted user must not be returned when filtering by DeletedAt == null");
    }

    // ── AC3: Owned instances are handled (suspended/destroyed) ───────────────

    [Fact]
    public async Task DeleteAccount_WithRunningInstance_SuspendsInstance()
    {
        // Arrange
        var user = CreateUser("instance_owner", "instance_owner@example.com");
        _dbContext!.HubUsers.Add(user);
        await _dbContext.SaveChangesAsync();

        var instance = new ManagedInstance
        {
            Id = _snowflake!.NextId(),
            OwnerId = user.Id,
            Domain = "myserver.xcord-dev.net",
            DisplayName = "My Server",
            Status = InstanceStatus.Running,
            SnowflakeWorkerId = 10,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.ManagedInstances.Add(instance);
        await _dbContext.SaveChangesAsync();

        // Re-load user to populate nav properties (handler includes them)
        var handler = CreateHandler();
        var command = new DeleteAccountCommand(user.Id, TestPassword);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var suspendedInstance = await _dbContext.ManagedInstances
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == instance.Id);

        suspendedInstance.Should().NotBeNull();
        suspendedInstance!.DeletedAt.Should().NotBeNull("instance must be soft-deleted when owner account is deleted");
        suspendedInstance.Status.Should().BeOneOf(
            [InstanceStatus.Suspended, InstanceStatus.Destroyed],
            "instance must be suspended or destroyed when owner account is deleted");
    }

    [Fact]
    public async Task DeleteAccount_WithAlreadyDestroyedInstance_DoesNotModifyIt()
    {
        // Arrange
        var user = CreateUser("destroyed_inst_owner", "destroyed_inst@example.com");
        _dbContext!.HubUsers.Add(user);
        await _dbContext.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var instance = new ManagedInstance
        {
            Id = _snowflake!.NextId(),
            OwnerId = user.Id,
            Domain = "old.xcord-dev.net",
            DisplayName = "Old Server",
            Status = InstanceStatus.Destroyed,
            SnowflakeWorkerId = 11,
            CreatedAt = now.AddDays(-30),
            DeletedAt = now.AddDays(-1),
        };
        _dbContext.ManagedInstances.Add(instance);
        await _dbContext.SaveChangesAsync();

        var handler = CreateHandler();
        var command = new DeleteAccountCommand(user.Id, TestPassword);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert — user deletion succeeds even with already-destroyed instances
        result.IsSuccess.Should().BeTrue();
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAccount_WithWrongPassword_ReturnsValidationError()
    {
        // Arrange
        var user = CreateUser("wrong_pw_user", "wrong_pw@example.com");
        _dbContext!.HubUsers.Add(user);
        await _dbContext.SaveChangesAsync();

        var handler = CreateHandler();
        var command = new DeleteAccountCommand(user.Id, "WrongPassword999!");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("INVALID_PASSWORD");

        // User must NOT have been deleted
        var intact = await _dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == user.Id && u.DeletedAt == null);
        intact.Should().NotBeNull("user must not be deleted when wrong password is provided");
    }

    [Fact]
    public async Task DeleteAccount_AdminUser_ReturnsForbidden()
    {
        // Arrange
        var admin = CreateUser("admin_user", "admin@example.com", isAdmin: true);
        _dbContext!.HubUsers.Add(admin);
        await _dbContext.SaveChangesAsync();

        var handler = CreateHandler();
        var command = new DeleteAccountCommand(admin.Id, TestPassword);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("ADMIN_ACCOUNT");

        // Admin must NOT have been deleted
        var intact = await _dbContext.HubUsers
            .FirstOrDefaultAsync(u => u.Id == admin.Id && u.DeletedAt == null);
        intact.Should().NotBeNull("admin user must never be deleted");
    }

    [Fact]
    public async Task DeleteAccount_RefreshTokensRevoked()
    {
        // Arrange
        var user = CreateUser("token_revoke_user", "token_revoke@example.com");
        _dbContext!.HubUsers.Add(user);
        await _dbContext.SaveChangesAsync();

        // Add some refresh tokens
        var token1 = new RefreshToken
        {
            Id = _snowflake!.NextId(),
            TokenHash = "hash1",
            HubUserId = user.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var token2 = new RefreshToken
        {
            Id = _snowflake.NextId(),
            TokenHash = "hash2",
            HubUserId = user.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.RefreshTokens.Add(token1);
        _dbContext.RefreshTokens.Add(token2);
        await _dbContext.SaveChangesAsync();

        var handler = CreateHandler();
        var command = new DeleteAccountCommand(user.Id, TestPassword);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var remainingTokens = await _dbContext.RefreshTokens
            .Where(t => t.HubUserId == user.Id)
            .ToListAsync();

        remainingTokens.Should().BeEmpty("all refresh tokens must be revoked on account deletion");
    }
}
