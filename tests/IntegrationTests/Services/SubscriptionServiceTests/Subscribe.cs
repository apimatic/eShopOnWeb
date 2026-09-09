#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services.SubscriptionServiceTests;

public class Subscribe
{
    private static readonly BuyerIdentity Buyer = new("buyer-1", "buyer1@example.com", "buyer1", "buyer1");

    private readonly IMaxioBillingClient _billingClient = Substitute.For<IMaxioBillingClient>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly EfRepository<SubscriptionRecord> _repository;

    private static readonly SubscriptionPlan Plan =
        new(1, "eshop-pro", "Pro Plan", null, 29900, 1, "month", false);

    public Subscribe()
    {
        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: "TestSubscriptionCatalog")
            .Options;
        _repository = new EfRepository<SubscriptionRecord>(new CatalogContext(dbOptions));
    }

    private SubscriptionService BuildService() =>
        new(_billingClient, _repository, _logger);

    [Fact]
    public async Task CreatesCustomerAndSubscriptionOnFirstSubscribe()
    {
        _billingClient.ListPlansAsync().Returns(new[] { Plan });
        _billingClient.FindCustomerByReferenceAsync(Arg.Any<string>()).Returns((MaxioCustomer?)null);
        _billingClient.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(new MaxioCustomer(10, "eshopweb:buyer1@example.com", "buyer1", "buyer1", "buyer1@example.com"));
        _billingClient.CreateSubscriptionAsync(10, "eshop-pro")
            .Returns(LiveSubscription(100));

        var result = await BuildService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.True(result.CreatedNew);
        Assert.Equal(100, result.Subscription.BillingSubscriptionId);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(10, result.Subscription.BillingCustomerId);
        await _billingClient.Received(1).CreateCustomerAsync("eshopweb:buyer1@example.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>());
        await _billingClient.Received(1).CreateSubscriptionAsync(10, "eshop-pro");
    }

    [Fact]
    public async Task RepeatedSubscribeReturnsExistingSubscriptionWithoutCreatingADuplicate()
    {
        _billingClient.ListPlansAsync().Returns(new[] { Plan });
        _billingClient.FindCustomerByReferenceAsync(Arg.Any<string>()).Returns(Customer());
        _billingClient.ListCustomerSubscriptionsAsync(10).Returns(new[] { LiveSubscription(100) });

        var first = await BuildService().SubscribeAsync(Buyer, "eshop-pro");
        var second = await BuildService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.Equal(first.Subscription.BillingSubscriptionId, second.Subscription.BillingSubscriptionId);
        Assert.False(second.CreatedNew);
        await _billingClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ReSubscribeAfterCancellationCreatesANewSubscription()
    {
        _billingClient.ListPlansAsync().Returns(new[] { Plan });
        _billingClient.FindCustomerByReferenceAsync(Arg.Any<string>()).Returns(Customer());
        _billingClient.CreateSubscriptionAsync(10, "eshop-pro")
            .Returns(LiveSubscription(200));
        _billingClient.ListCustomerSubscriptionsAsync(10)
            .Returns(new[] { CancelledSubscription(100) });

        var result = await BuildService().SubscribeAsync(Buyer, "eshop-pro");

        Assert.True(result.CreatedNew);
        Assert.Equal(200, result.Subscription.BillingSubscriptionId);
    }

    [Fact]
    public async Task UnknownPlanThrows()
    {
        _billingClient.ListPlansAsync().Returns(new[] { Plan });

        await Assert.ThrowsAsync<PlanNotFoundException>(() => BuildService().SubscribeAsync(Buyer, "missing-plan"));
    }

    [Fact]
    public async Task ListForBuyerReturnsEmptyWhenNoCustomerExists()
    {
        _billingClient.FindCustomerByReferenceAsync(Arg.Any<string>()).Returns((MaxioCustomer?)null);

        var subscriptions = await BuildService().ListForBuyerAsync(Buyer);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListForBuyerReturnsLiveStateFromBillingSystem()
    {
        _billingClient.FindCustomerByReferenceAsync(Arg.Any<string>()).Returns(Customer());
        _billingClient.ListCustomerSubscriptionsAsync(10).Returns(new[] { LiveSubscription(100) });

        var subscriptions = (await BuildService().ListForBuyerAsync(Buyer)).ToList();

        var single = Assert.Single(subscriptions);
        Assert.Equal(100, single.BillingSubscriptionId);
        Assert.Equal("active", single.State);
    }

    private static MaxioCustomer Customer() =>
        new(10, "eshopweb:buyer1@example.com", "buyer1", "buyer1", "buyer1@example.com");

    private static MaxioSubscriptionInfo LiveSubscription(int id) =>
        new(id, "active", "eshop-pro", "Pro Plan", 29900,
            CurrentPeriodStartsAt: DateTime.UtcNow,
            CurrentPeriodEndsAt: DateTime.UtcNow.AddMonths(1),
            NextBillingAt: DateTime.UtcNow.AddMonths(1),
            CreatedAt: DateTime.UtcNow,
            CanceledAt: null);

    private static MaxioSubscriptionInfo CancelledSubscription(int id) =>
        new(id, "canceled", "eshop-pro", "Pro Plan", 29900,
            CurrentPeriodStartsAt: null,
            CurrentPeriodEndsAt: null,
            NextBillingAt: null,
            CreatedAt: DateTime.UtcNow.AddMonths(-2),
            CanceledAt: DateTime.UtcNow.AddMonths(-1));
}
