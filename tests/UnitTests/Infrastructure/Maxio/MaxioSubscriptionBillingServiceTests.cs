using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using MaxioProduct = Microsoft.eShopWeb.Infrastructure.Maxio.Models.Product;
using MaxioCustomer = Microsoft.eShopWeb.Infrastructure.Maxio.Models.Customer;
using MaxioSubscription = Microsoft.eShopWeb.Infrastructure.Maxio.Models.Subscription;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioApiClient _maxio = Substitute.For<IMaxioApiClient>();
    private readonly ILogger<MaxioSubscriptionBillingService> _logger = Substitute.For<ILogger<MaxioSubscriptionBillingService>>();
    private readonly Shopper _shopper = new("user-123", "demouser@microsoft.com", "demouser@microsoft.com");

    private MaxioSubscriptionBillingService CreateService()
    {
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" });
        return new MaxioSubscriptionBillingService(_maxio, options, _logger);
    }

    [Fact]
    public void SplitNameUsesEmailLocalPart()
    {
        var (first, last) = MaxioSubscriptionBillingService.SplitName(_shopper);
        Assert.Equal("Demouser", first);
        Assert.Equal("Customer", last);
    }

    [Fact]
    public void SubscriptionReferenceCombinesCustomerAndPlan()
    {
        Assert.Equal("99:eshop-pro", MaxioSubscriptionBillingService.BuildSubscriptionReference(99, "eshop-pro"));
    }

    [Fact]
    public async Task ListPlansExcludesArchivedAndMapsCents()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new() { Handle = "old", Name = "Old", PriceInCents = 100, Interval = 1, IntervalUnit = "month", ArchivedAt = "2020-01-01T00:00:00Z" }
        });

        var plans = await CreateService().ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
    }

    [Fact]
    public async Task SubscribeCreatesCustomerAndSubscription()
    {
        ArrangeProPlan();
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, default).Returns((MaxioCustomer?)null);
        _maxio.ListCustomersAsync(_shopper.Email, default).Returns(new List<MaxioCustomer>());
        _maxio.CreateCustomerAsync(Arg.Any<CreateCustomer>(), default).Returns(new MaxioCustomer { Id = 42, Email = _shopper.Email, Reference = _shopper.UserId });
        _maxio.FindSubscriptionByReferenceAsync("42:eshop-pro", default).Returns((MaxioSubscription?)null);
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), default).Returns(new MaxioSubscription
        {
            Id = 7,
            State = "active",
            ProductPriceInCents = 29900,
            NextAssessmentAt = "2026-09-19T00:00:00Z",
            Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
        });

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(7, result.Subscription.Id);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingDate);

        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomer>(c => c.Email == _shopper.Email && c.Reference == _shopper.UserId),
            default);
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(s =>
                s.ProductHandle == "eshop-pro"
                && s.CustomerId == 42
                && s.Reference == "42:eshop-pro"
                && s.PaymentCollectionMethod == "remittance"),
            default);
    }

    [Fact]
    public async Task SubscribeIsIdempotentWhenSubscriptionAlreadyExists()
    {
        ArrangeProPlan();
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, default).Returns(new MaxioCustomer { Id = 42, Email = _shopper.Email, Reference = _shopper.UserId });
        _maxio.FindSubscriptionByReferenceAsync("42:eshop-pro", default).Returns(new MaxioSubscription
        {
            Id = 7,
            State = "active",
            ProductPriceInCents = 29900,
            Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
        });

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(7, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), default);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), default);
    }

    [Fact]
    public async Task SubscribeFindsCustomerByEmailWhenReferenceMisses()
    {
        ArrangeProPlan();
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, default).Returns((MaxioCustomer?)null);
        _maxio.ListCustomersAsync(_shopper.Email, default).Returns(new List<MaxioCustomer>
        {
            new() { Id = 42, Email = _shopper.Email, Reference = "old-id" }
        });
        _maxio.FindSubscriptionByReferenceAsync("42:eshop-pro", default).Returns(new MaxioSubscription
        {
            Id = 7,
            State = "active",
            ProductPriceInCents = 29900,
            Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
        });

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), default);
    }

    [Fact]
    public async Task SubscribeThrowsWhenPlanIsNotInFamily()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            CreateService().SubscribeAsync(_shopper, "not-a-plan"));
    }

    [Fact]
    public async Task ListShopperSubscriptionsReturnsEmptyWhenNoCustomer()
    {
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, default).Returns((MaxioCustomer?)null);
        _maxio.ListCustomersAsync(_shopper.Email, default).Returns(new List<MaxioCustomer>());

        var subscriptions = await CreateService().ListShopperSubscriptionsAsync(_shopper);

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), default);
    }

    private void ArrangeProPlan()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });
    }
}
