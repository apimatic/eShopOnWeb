using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _client = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly ILogger<MaxioSubscriptionBillingService> _logger = Substitute.For<ILogger<MaxioSubscriptionBillingService>>();
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "Demo", "User");
    private readonly MaxioSubscriptionBillingService _sut;

    public MaxioSubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" });
        _sut = new MaxioSubscriptionBillingService(_client, options, _logger);
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsActiveProductsInConfiguredFamily()
    {
        _client.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
                new() { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
                new() { Handle = "old-plan", Name = "Archived", PriceInCents = 100, Interval = 1, IntervalUnit = "month", ArchivedAt = System.DateTimeOffset.UtcNow }
            });

        var plans = await _sut.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, plan => plan.Handle == "eshop-pro" && plan.Price == 299.00m);
        Assert.Contains(plans, plan => plan.Handle == "basic-plan" && plan.Price == 29.00m);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _client.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 });
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerPayload>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionPayload>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                ProductHandle = "eshop-pro",
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" },
                NextAssessmentAt = System.DateTimeOffset.Parse("2026-09-19T00:00:00Z")
            });

        var result = await _sut.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal("Pro Plan", result.PlanName);
        Assert.Equal(299.00m, result.Price);
        Assert.Equal("active", result.State);
        Assert.NotNull(result.NextBillingDate);

        await _client.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomerPayload>(payload => payload.Reference == "user-1" && payload.Email == _shopper.Email),
            Arg.Any<CancellationToken>());
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionPayload>(payload =>
                payload.CustomerId == 42 &&
                payload.ProductHandle == "eshop-pro" &&
                payload.PaymentCollectionMethod == "remittance" &&
                payload.Reference == MaxioSubscriptionBillingService.BuildSubscriptionReference("user-1", "eshop-pro")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenLiveSubscriptionAlreadyExists()
    {
        _client.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 });
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new()
                {
                    Id = 77,
                    State = "active",
                    ProductPriceInCents = 29900,
                    Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
                }
            });

        var first = await _sut.SubscribeAsync(_shopper, "eshop-pro");
        var second = await _sut.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(77, first.Id);
        Assert.Equal(77, second.Id);
        await _client.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerPayload>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionPayload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReusesCustomerWhenCreateRacesOnReference()
    {
        _client.GetProductByHandleAsync("basic-plan", Arg.Any<CancellationToken>())
            .Returns(new MaxioProduct { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900 });
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 42, Reference = "user-1" });
        _client.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerPayload>(), Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw new MaxioApiException(422, "reference must be unique"));
        _client.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _client.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);
        _client.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionPayload>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 12,
                State = "active",
                ProductPriceInCents = 2900,
                ProductHandle = "basic-plan",
                Product = new MaxioProduct { Handle = "basic-plan", Name = "Basic Plan" }
            });

        var result = await _sut.SubscribeAsync(_shopper, "basic-plan");

        Assert.Equal(12, result.Id);
        await _client.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionPayload>(payload => payload.CustomerId == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenPlanIsMissing()
    {
        _client.GetProductByHandleAsync("no-such-plan", Arg.Any<CancellationToken>())
            .Returns((MaxioProduct?)null);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => _sut.SubscribeAsync(_shopper, "no-such-plan"));
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _client.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await _sut.ListSubscriptionsAsync("user-1");

        Assert.Empty(result);
        await _client.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
