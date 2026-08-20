using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class SubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly SubscriptionConcurrencyGate _gate = new();
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "Demouser", "Customer");

    private SubscriptionBillingService CreateSut()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });

        return new SubscriptionBillingService(_maxio, options, _gate, NullLogger<SubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task ListPlans_ReturnsActiveProductsInFamily()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<Product>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new() { Handle = "archived", Name = "Old", PriceInCents = 1, ArchivedAt = DateTimeOffset.UtcNow }
        });

        var plans = await CreateSut().ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription()
    {
        SeedPlan("eshop-pro");
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((Customer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>()).Returns(new Customer { Id = 10, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Subscription>());
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Subscription?)null);
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>()).Returns(new Subscription
        {
            Id = 99,
            State = "active",
            ProductPriceInCents = 29900,
            NextAssessmentAt = DateTimeOffset.UtcNow.AddMonths(1),
            Product = new Product { Handle = "eshop-pro", Name = "Pro Plan" }
        });

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(29900, result.PriceInCents);
        Assert.NotNull(result.NextBillingAt);

        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateCustomer>(c => c.Reference == _shopper.UserId && c.Email == _shopper.Email),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateSubscription>(s =>
                s.ProductHandle == "eshop-pro"
                && s.CustomerId == 10
                && s.PaymentCollectionMethod == "remittance"
                && s.Reference == SubscriptionBillingService.BuildSubscriptionReference(_shopper.UserId, "eshop-pro")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_IsIdempotentWhenLiveSubscriptionExists()
    {
        SeedPlan("eshop-pro");
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns(new Customer { Id = 10, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Subscription>
        {
            new()
            {
                Id = 77,
                State = "active",
                ProductPriceInCents = 29900,
                Product = new Product { Handle = "eshop-pro", Name = "Pro Plan" }
            }
        });

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(77, result.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomer>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReturnsNotFoundForUnknownPlan()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<Product>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });

        var ex = await Assert.ThrowsAsync<BillingException>(() => CreateSut().SubscribeAsync(_shopper, "no-such-plan"));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((Customer?)null);

        var result = await CreateSut().ListMySubscriptionsAsync(_shopper);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private void SeedPlan(string handle)
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<Product>
        {
            new()
            {
                Handle = handle,
                Name = "Pro Plan",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month",
                ProductFamily = new ProductFamily { Handle = "eshop-subscribe" }
            }
        });
    }
}
