using FluentAssertions;
using XcordHub.Entities;
using XcordHub.Features.Billing;
using XcordHub.Infrastructure.Services;
using XcordHub.Tests.Infrastructure.Fixtures;

namespace XcordHub.Tests.Infrastructure.Billing;

/// <summary>
/// Tests for GetInvoicesHandler -- the read-side / refund-history flow.
/// The handler proxies to Stripe via IStripeService (stubbed); these tests
/// verify the no-Stripe code path and the customer-id wiring.
/// </summary>
[Collection("SharedPostgres")]
[Trait("Category", "Integration")]
public sealed class RefundTests : BillingTestsBase
{
    public RefundTests(SharedPostgresFixture fixture) : base(fixture, "xcordhub_billing_refund_test") { }

    [Fact]
    public async Task GetInvoices_NoStripeConfigured_ReturnsEmptyList()
    {
        await using var dbContext = CreateDbContext();
        var (user, _) = await SeedInstanceAsync(dbContext, UserIdBase + 30, "invoices_nostripe");

        var handler = new GetInvoicesHandler(
            dbContext,
            StubUser(user.Id),
            NoStripeOptions(),
            new NoOpStripeService());

        var result = await handler.Handle(new GetInvoicesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Invoices.Should().BeEmpty(
            "when Stripe is not configured the handler must return an empty invoice list");
    }

    [Fact]
    public async Task GetInvoices_NoStripeCustomerId_ReturnsEmptyList()
    {
        await using var dbContext = CreateDbContext();
        var (user, _) = await SeedInstanceAsync(dbContext, UserIdBase + 31, "invoices_nocustomer");

        // User has no StripeCustomerId (default)
        var handler = new GetInvoicesHandler(
            dbContext,
            StubUser(user.Id),
            FakeStripeOptions(),
            new NoOpStripeService());

        var result = await handler.Handle(new GetInvoicesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Invoices.Should().BeEmpty(
            "when user has no Stripe customer ID the handler must return an empty list without calling Stripe");
    }

    [Fact]
    public async Task GetInvoices_WithStripeCustomer_ReturnsInvoiceList()
    {
        await using var dbContext = CreateDbContext();
        var (user, _) = await SeedInstanceAsync(dbContext, UserIdBase + 32, "invoices_withcustomer");

        // Assign a fake Stripe customer ID to the user
        var dbUser = await dbContext.HubUsers.FindAsync(user.Id);
        dbUser!.StripeCustomerId = "cus_test_fake_abc";
        await dbContext.SaveChangesAsync();

        var fakeInvoice = new StripeInvoice(
            Id: "in_test_001",
            Description: "Subscription invoice",
            AmountCents: 4500,
            Currency: "usd",
            Status: "paid",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-5),
            PdfUrl: "https://pay.stripe.com/invoice/in_test_001/pdf");

        var stripeStub = new SpyStripeService("url", new List<StripeInvoice> { fakeInvoice });

        var handler = new GetInvoicesHandler(
            dbContext,
            StubUser(user.Id),
            FakeStripeOptions(),
            stripeStub);

        var result = await handler.Handle(new GetInvoicesQuery(Limit: 10), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Invoices.Should().HaveCount(1);

        var invoice = result.Value.Invoices[0];
        invoice.Id.Should().Be("in_test_001");
        invoice.AmountCents.Should().Be(4500);
        invoice.Currency.Should().Be("usd");
        invoice.Status.Should().Be("paid");
        invoice.PdfUrl.Should().Contain("in_test_001");

        stripeStub.GetInvoicesCalled.Should().BeTrue();
        stripeStub.LastGetInvoicesCustomerId.Should().Be("cus_test_fake_abc");
    }

    [Fact]
    public async Task GetInvoices_UnknownUser_ReturnsNotFound()
    {
        await using var dbContext = CreateDbContext();

        var handler = new GetInvoicesHandler(
            dbContext,
            StubUser(999_000_000_001L),
            NoStripeOptions(),
            new NoOpStripeService());

        var result = await handler.Handle(new GetInvoicesQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("USER_NOT_FOUND");
    }
}
