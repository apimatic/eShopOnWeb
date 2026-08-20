using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly MaxioSubscriptionBillingService _sut;
    private readonly ShopperIdentity _shopper = new("user-123", "demouser@microsoft.com", "Demo", "User");

    public MaxioSubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });

        _sut = new MaxioSubscriptionBillingService(_maxio, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsFamilyProducts()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProductSnapshot>
            {
                new()
                {
                    Id = 1,
                    Handle = "eshop-pro",
                    Name = "Pro Plan",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month",
                    ProductFamilyHandle = "eshop-subscribe"
                },
                new()
                {
                    Id = 2,
                    Handle = "basic-plan",
                    Name = "Basic Plan",
                    PriceInCents = 2900,
                    Interval = 1,
                    IntervalUnit = "month",
                    ProductFamilyHandle = "eshop-subscribe"
                }
            });

        var plans = await _sut.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal(29.00m, plans[0].Price);
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal(299.00m, plans[1].Price);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerSnapshot?)null);

        var subscriptions = await _sut.ListMySubscriptionsAsync(_shopper);

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        SetupPlan("eshop-pro");
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomerSnapshot?)null, new MaxioCustomerSnapshot { Id = 44, Reference = _shopper.UserId });
        _maxio.CreateCustomerAsync(_shopper.FirstName, _shopper.LastName, _shopper.Email, _shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerSnapshot { Id = 44, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(44, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionSnapshot>());
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscriptionSnapshot?)null);
        _maxio.CreateSubscriptionAsync(44, "eshop-pro", Arg.Any<string?>(), "remittance", Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscriptionSnapshot
            {
                Id = 9001,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                ProductPriceInCents = 29900,
                NextAssessmentAt = System.DateTimeOffset.Parse("2026-09-20T00:00:00Z")
            });

        var result = await _sut.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(9001, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(299.00m, result.Price);
        Assert.NotNull(result.NextBillingAt);
        await _maxio.Received(1).CreateCustomerAsync(_shopper.FirstName, _shopper.LastName, _shopper.Email, _shopper.UserId, Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(44, "eshop-pro", Arg.Any<string?>(), "remittance", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentForExistingLiveSubscription()
    {
        SetupPlan("eshop-pro");
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomerSnapshot { Id = 44, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(44, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscriptionSnapshot>
            {
                new()
                {
                    Id = 11,
                    State = "active",
                    ProductHandle = "eshop-pro",
                    ProductName = "Pro Plan",
                    ProductPriceInCents = 29900
                }
            });

        var first = await _sut.SubscribeAsync(_shopper, "eshop-pro");
        var second = await _sut.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(11, first.Id);
        Assert.Equal(11, second.Id);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_RejectsUnknownPlan()
    {
        SetupPlan("eshop-pro");

        await Assert.ThrowsAsync<BillingValidationException>(() => _sut.SubscribeAsync(_shopper, "not-a-plan"));
    }

    [Fact]
    public void BuildSubscriptionReference_IsStableAndSafe()
    {
        var reference = MaxioSubscriptionBillingService.BuildSubscriptionReference("abc/def", "eshop-pro");
        Assert.Equal("eshop-abc-def-eshop-pro", reference);
        Assert.Equal(reference, MaxioSubscriptionBillingService.BuildSubscriptionReference("abc/def", "eshop-pro"));
    }

    private void SetupPlan(string handle)
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProductSnapshot>
            {
                new()
                {
                    Id = 7,
                    Handle = handle,
                    Name = "Pro Plan",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month",
                    ProductFamilyHandle = "eshop-subscribe"
                }
            });
    }
}
