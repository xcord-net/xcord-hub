using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using XcordHub.Features.MailingList;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Validation tests for AddMailingListEntryHandler.
/// Validate is a pure method that does not touch the database, so no Testcontainers setup is required.
/// The handler is instantiated with a DbContext that has a dummy connection string - no connection is
/// ever opened because Validate never issues a query.
/// </summary>
public sealed class MailingListTests
{
    private const string TestEncryptionKey = "mailing-list-tests-encryption-key-with-256-bits-ok!!";

    private static AddMailingListEntryHandler BuildHandler()
    {
        // A dummy connection string is sufficient here - Validate never makes a DB call.
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql("Host=localhost;Database=_dummy_mailing_list_test;Username=postgres;Password=postgres")
            .Options;

        var dbContext = new HubDbContext(options, new AesEncryptionService(TestEncryptionKey));
        return new AddMailingListEntryHandler(dbContext, new SnowflakeIdGenerator(256));
    }

    // ---------------------------------------------------------------------------
    // Valid tiers - Validate must return null
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Basic")]
    [InlineData("Pro")]
    [InlineData("Enterprise")]
    [InlineData("Voice & Video")]
    [InlineData("android app")]
    public void Validate_ValidTier_ReturnsNull(string tier)
    {
        var handler = BuildHandler();
        var request = new AddMailingListEntryRequest("test@example.com", tier);

        var error = handler.Validate(request);

        error.Should().BeNull(
            because: $"'{tier}' is a recognised tier and must pass validation");
    }

    // ---------------------------------------------------------------------------
    // Invalid tiers - Validate must return a non-null error
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Chat (10 users)")]
    [InlineData("Chat + Audio (50 users)")]
    [InlineData("InvalidTier")]
    public void Validate_InvalidTier_ReturnsError(string tier)
    {
        var handler = BuildHandler();
        var request = new AddMailingListEntryRequest("test@example.com", tier);

        var error = handler.Validate(request);

        error.Should().NotBeNull(
            because: $"'{tier}' is not a recognised tier and must be rejected");
        error!.Code.Should().Be("VALIDATION_FAILED");
    }
}
