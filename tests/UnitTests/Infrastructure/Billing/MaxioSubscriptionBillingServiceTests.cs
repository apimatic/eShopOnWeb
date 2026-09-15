using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
    private readonly MaxioSubscriptionBillingService _service;

    public MaxioSubscriptionBillingServiceTests()
    {
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" });
        _service = new MaxioSubscriptionBillingService(_maxio, options, _logger);

        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new[]
        {
            new Product { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });
    }

    [Fact]
    public async Task ListAvailablePlansAsync_MapsPriceFromCents()
    {
        var plans = await _service.ListAvailablePlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerOnceAndReturnsExistingLiveSubscription()
    {
        _maxio.ReadCustomerByReferenceAsync("buyer-1", Arg.Any<CancellationToken>())
            .Returns(new Customer { Id = 9, Reference = "buyer-1", Email = "demouser@microsoft.com" });
        _maxio.ListCustomerSubscriptionsAsync(9, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new Subscription
            {
                Id = 55,
                State = "active",
                ProductPriceInCents = 29900,
                Product = new Product { Handle = "eshop-pro", Name = "Pro Plan" }
            }
        });

        var first = await _service.SubscribeAsync("buyer-1", "demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro");
        var second = await _service.SubscribeAsync("buyer-1", "demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro");

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(55, first.Subscription.Id);
        Assert.Equal(299.00m, first.Subscription.Price);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionWhenMissing()
    {
        _maxio.ReadCustomerByReferenceAsync("buyer-1", Arg.Any<CancellationToken>()).Returns((Customer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new Customer { Id = 9, Reference = "buyer-1" });
        _maxio.ListCustomerSubscriptionsAsync(9, Arg.Any<CancellationToken>()).Returns(System.Array.Empty<Subscription>());
        _maxio.FindSubscriptionByReferenceAsync("eshop:buyer-1:eshop-pro", Arg.Any<CancellationToken>()).Returns((Subscription?)null);
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>()).Returns(new Subscription
        {
            Id = 77,
            State = "active",
            ProductPriceInCents = 29900,
            NextAssessmentAt = System.DateTimeOffset.Parse("2026-09-19T00:00:00Z"),
            Product = new Product { Handle = "eshop-pro", Name = "Pro Plan" }
        });

        var result = await _service.SubscribeAsync("buyer-1", "demouser@microsoft.com", "demouser@microsoft.com", "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(77, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomer>(customer => customer.Reference == "buyer-1" && customer.Email == "demouser@microsoft.com"),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(subscription =>
                subscription.ProductHandle == "eshop-pro" &&
                subscription.CustomerId == 9 &&
                subscription.Reference == "eshop:buyer-1:eshop-pro" &&
                subscription.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_RejectsUnknownPlanHandle()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            _service.SubscribeAsync("buyer-1", "demouser@microsoft.com", "demouser@microsoft.com", "not-a-plan"));
    }

    [Fact]
    public async Task ListSubscriptionsForBuyerAsync_ReturnsEmptyWhenCustomerMissing()
    {
        _maxio.ReadCustomerByReferenceAsync("buyer-1", Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var subscriptions = await _service.ListSubscriptionsForBuyerAsync("buyer-1");

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
