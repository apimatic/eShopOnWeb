using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class SubscriptionBillingServiceTests
{
    private static readonly ShopperIdentity Shopper =
        new("user-guid", "demouser@microsoft.com", "demouser", "Shopper");

    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly SubscriptionBillingService _sut;

    public SubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe"
        });
        _sut = new SubscriptionBillingService(_maxio, options, NullLogger<SubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task ListPlans_ReturnsNonArchivedFamilyProducts()
    {
        _maxio.ListProductsForProductFamilyAsync("handle:eshop-subscribe", 1, 200, false, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Id = 1, Handle = "eshop-pro", Name = "Pro", PriceInCents = 29900, Interval = 1, IntervalUnit = "month", RequireCreditCard = false },
                new() { Id = 2, Handle = "old", Name = "Old", PriceInCents = 100, ArchivedAt = System.DateTimeOffset.UtcNow }
            });

        var plans = await _sut.ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNew()
    {
        OfferProPlan();
        _maxio.ReadCustomerByReferenceAsync("user-guid", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 9, Reference = "user-guid" });
        _maxio.FindSubscriptionByReferenceAsync("user-guid:eshop-pro", Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);
        _maxio.ListCustomerSubscriptionsAsync(9, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 77,
                State = "active",
                ProductPriceInCents = 29900,
                Reference = "user-guid:eshop-pro",
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
            });

        var result = await _sut.SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(77, result.Subscription.Id);
        Assert.Equal(299.00m, result.Subscription.Price);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomerRequest>(r => r.Customer.Reference == "user-guid"),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(r =>
                r.Subscription.ProductHandle == "eshop-pro"
                && r.Subscription.CustomerId == 9
                && r.Subscription.Reference == "user-guid:eshop-pro"
                && r.Subscription.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenLiveSubscriptionAlreadyExists()
    {
        OfferProPlan();
        _maxio.ReadCustomerByReferenceAsync("user-guid", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 9, Reference = "user-guid" });
        _maxio.FindSubscriptionByReferenceAsync("user-guid:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 77,
                State = "active",
                ProductPriceInCents = 29900,
                Reference = "user-guid:eshop-pro",
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
            });

        var first = await _sut.SubscribeAsync(Shopper, "eshop-pro");
        var second = await _sut.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(77, second.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReturnsExisting_WhenCreateLosesRace()
    {
        OfferProPlan();
        _maxio.ReadCustomerByReferenceAsync("user-guid", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 9, Reference = "user-guid" });
        _maxio.FindSubscriptionByReferenceAsync("user-guid:eshop-pro", Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null, new MaxioSubscription
            {
                Id = 77,
                State = "active",
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro" }
            });
        _maxio.ListCustomerSubscriptionsAsync(9, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns<MaxioSubscription>(_ => throw new MaxioApiException(HttpStatusCode.UnprocessableEntity, "taken"));

        var result = await _sut.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(77, result.Subscription.Id);
    }

    [Fact]
    public async Task Subscribe_RejectsUnknownPlan()
    {
        _maxio.ListProductsForProductFamilyAsync("handle:eshop-subscribe", 1, 200, false, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro" }
            });

        await Assert.ThrowsAsync<BillingValidationException>(() => _sut.SubscribeAsync(Shopper, "not-a-plan"));
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        _maxio.ReadCustomerByReferenceAsync("user-guid", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var subscriptions = await _sut.ListMySubscriptionsAsync(Shopper);

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CanonicalSubscriptionReference_IsStable()
    {
        Assert.Equal("abc:eshop-pro", SubscriptionBillingService.CanonicalSubscriptionReference("abc", "eshop-pro"));
        Assert.True(SubscriptionBillingService.IsTerminal("canceled"));
        Assert.False(SubscriptionBillingService.IsTerminal("active"));
    }

    private void OfferProPlan()
    {
        _maxio.ListProductsForProductFamilyAsync("handle:eshop-subscribe", 1, 200, false, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new()
                {
                    Id = 1,
                    Handle = "eshop-pro",
                    Name = "Pro Plan",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month"
                }
            });
    }
}
