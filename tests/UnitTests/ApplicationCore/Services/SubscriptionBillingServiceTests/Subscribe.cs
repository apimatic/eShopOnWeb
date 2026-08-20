using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IMaxioBillingGateway _maxio = Substitute.For<IMaxioBillingGateway>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");
    private readonly BillingProduct _proPlan = new()
    {
        Id = 1,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private SubscriptionBillingService CreateService()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new SubscriptionBillingService(_maxio, _logger, options);
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenShopperIsNew()
    {
        _maxio.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(_proPlan);
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), _shopper.Email, _shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.UserId, Email = _shopper.Email });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);
        _maxio.CreateSubscriptionAsync("eshop-pro", 42, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingSubscription
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                Product = _proPlan
            });

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.NotNull(result.Subscription.NextBillingDate);
        await _maxio.Received(1).CreateCustomerAsync("demouser", "eShopOnWeb", _shopper.Email, _shopper.UserId, Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync("eshop-pro", 42, "eshop:user-1:eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingOpenSubscriptionWithoutCreatingAnother()
    {
        var existing = new BillingSubscription
        {
            Id = 7,
            State = "active",
            ProductPriceInCents = 29900,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddDays(20),
            Product = _proPlan,
            Customer = new BillingCustomer { Id = 42, Reference = _shopper.UserId }
        };

        _maxio.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(_proPlan);
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.UserId });
        _maxio.FindSubscriptionByReferenceAsync("eshop:user-1:eshop-pro", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(7, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversWhenCreateConflictsOnDoubleClick()
    {
        var recovered = new BillingSubscription
        {
            Id = 15,
            State = "active",
            ProductPriceInCents = 29900,
            Product = _proPlan
        };

        _maxio.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(_proPlan);
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.UserId });
        _maxio.FindSubscriptionByReferenceAsync("eshop:user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null, recovered);
        _maxio.CreateSubscriptionAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<BillingSubscription>(_ => throw new MaxioApiException(HttpStatusCode.Conflict, "duplicate"));

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(15, result.Subscription.Id);
    }

    [Fact]
    public async Task RejectsUnknownProductHandle()
    {
        _maxio.GetProductByHandleAsync("not-a-plan", Arg.Any<CancellationToken>()).Returns((BillingProduct?)null);

        await Assert.ThrowsAsync<BillingValidationException>(() => CreateService().SubscribeAsync(_shopper, "not-a-plan"));
    }

    [Fact]
    public async Task RejectsEmptyProductHandle()
    {
        await Assert.ThrowsAsync<BillingValidationException>(() => CreateService().SubscribeAsync(_shopper, "  "));
    }

    [Fact]
    public async Task ListPlansReturnsFamilyProductsOrderedByPrice()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<BillingProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro", PriceInCents = 29900, Interval = 1, IntervalUnit = "month", ProductFamilyHandle = "eshop-subscribe" },
            new() { Handle = "basic-plan", Name = "Basic", PriceInCents = 2900, Interval = 1, IntervalUnit = "month", ProductFamilyHandle = "eshop-subscribe" },
            new() { Handle = "old", Name = "Archived", PriceInCents = 100, Interval = 1, IntervalUnit = "month", ArchivedAt = DateTimeOffset.UtcNow, ProductFamilyHandle = "eshop-subscribe" }
        });

        var plans = await CreateService().ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal(29.00m, plans[0].Price);
        Assert.Equal("eshop-pro", plans[1].Handle);
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);

        var subscriptions = await CreateService().ListMySubscriptionsAsync(_shopper);

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListMySubscriptionsMapsMaxioRecords()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<BillingSubscription>
        {
            new()
            {
                Id = 9,
                State = "active",
                ProductPriceInCents = 2900,
                CurrentPeriodEndsAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
                Product = new BillingProduct { Handle = "basic-plan", Name = "Basic Plan" }
            }
        });

        var subscriptions = await CreateService().ListMySubscriptionsAsync(_shopper);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(9, subscription.Id);
        Assert.Equal("basic-plan", subscription.ProductHandle);
        Assert.Equal("active", subscription.State);
        Assert.Equal(29.00m, subscription.Price);
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T00:00:00Z"), subscription.NextBillingDate);
    }
}

public class MaxioOptionsTests
{
    [Fact]
    public void GetApiBaseAddressUsesSubdomainWhenBaseUrlIsEmpty()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-1" };
        Assert.Equal(new Uri("https://cp-exp-1.chargify.com/"), options.GetApiBaseAddress());
    }

    [Fact]
    public void GetApiBaseAddressUsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored",
            BaseUrl = "https://override.example.test/api"
        };
        Assert.Equal(new Uri("https://override.example.test/api/"), options.GetApiBaseAddress());
    }
}

public class SubscriptionStatesTests
{
    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", true)]
    [InlineData("canceled", false)]
    [InlineData("expired", false)]
    [InlineData("failed_to_create", false)]
    public void IsOpenMatchesLiveAndEndOfLifeStates(string state, bool expected)
    {
        Assert.Equal(expected, SubscriptionStates.IsOpen(state));
    }
}
