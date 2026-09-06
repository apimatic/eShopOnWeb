using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSubscriptionBillingServiceSubscribe
{
    private static readonly MaxioProduct ProPlan = new()
    {
        Id = 1,
        Name = "Pro Plan",
        Handle = "eshop-pro",
        PriceInCents = 29_900,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false,
        ProductFamily = new MaxioProductFamily { Id = 9, Handle = "eshop-subscribe" }
    };

    private static readonly MaxioProduct CardOnlyPlan = ProPlan with
    {
        Id = 2,
        Name = "Card Only",
        Handle = "card-only",
        RequireCreditCard = true
    };

    private static readonly MaxioProduct ArchivedPlan = ProPlan with
    {
        Id = 3,
        Name = "Retired",
        Handle = "retired",
        ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };

    private static readonly Subscriber Shopper = Subscriber.FromIdentity("demouser@microsoft.com");

    private static MaxioSubscriptionBillingService BuildService(FakeMaxioApiClient client, MaxioSettings? settings = null) =>
        new(
            client,
            new StaticOptionsMonitor<MaxioSettings>(settings ?? new MaxioSettings
            {
                ApiKey = "key",
                Subdomain = "acme",
                ProductFamilyHandle = "eshop-subscribe"
            }),
            new MemoryCache(new MemoryCacheOptions()),
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

    [Fact]
    public async Task CreatesTheCustomerAndTheSubscriptionOnFirstUse()
    {
        var client = new FakeMaxioApiClient(ProPlan);
        var service = BuildService(client);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("Pro Plan", result.Subscription.PlanName);
        Assert.Equal(299m, result.Subscription.Price);
        Assert.Equal("USD", result.Subscription.Currency);
        Assert.Equal("active", result.Subscription.State);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Equal(Shopper.Reference, result.Subscription.CustomerReference);
        Assert.Equal(1, client.CreateCustomerCallCount);
        Assert.Equal(1, client.CreateSubscriptionCallCount);
    }

    [Fact]
    public async Task ReusesTheExistingCustomerOnASecondPlan()
    {
        var client = new FakeMaxioApiClient(ProPlan, CardOnlyPlan with { Handle = "basic-plan", RequireCreditCard = false });
        var service = BuildService(client);

        await service.SubscribeAsync(Shopper, "eshop-pro");
        await service.SubscribeAsync(Shopper, "basic-plan");

        Assert.Equal(1, client.CreateCustomerCallCount);
        Assert.Equal(2, client.CreateSubscriptionCallCount);
    }

    [Fact]
    public async Task ReturnsTheExistingSubscriptionInsteadOfEnrollingTwice()
    {
        var client = new FakeMaxioApiClient(ProPlan);
        var service = BuildService(client);

        var first = await service.SubscribeAsync(Shopper, "eshop-pro");
        var second = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, client.CreateSubscriptionCallCount);
    }

    [Fact]
    public async Task CollapsesConcurrentDoubleClicksIntoASingleEnrollment()
    {
        var client = new FakeMaxioApiClient(ProPlan) { CallLatency = TimeSpan.FromMilliseconds(15) };
        var service = BuildService(client);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.SubscribeAsync(Shopper, "eshop-pro")));

        Assert.Equal(1, client.CreateSubscriptionCallCount);
        Assert.Equal(1, client.CreateCustomerCallCount);
        Assert.Single(results.Where(result => !result.AlreadyExisted));
        Assert.Single(results.Select(result => result.Subscription.Id).Distinct());
    }

    [Fact]
    public async Task ReRegistersTheCustomerThatAnotherCallerCreatedFirst()
    {
        var client = new FakeMaxioApiClient(ProPlan) { SimulateCustomerReferenceRace = true };
        var service = BuildService(client);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1, client.CreateSubscriptionCallCount);
    }

    [Fact]
    public async Task EnrollsAgainAfterAPreviousSubscriptionEnded()
    {
        var client = new FakeMaxioApiClient(ProPlan);
        var customer = client.SeedCustomer(Shopper.Reference);
        client.SeedSubscription(customer.Id, ProPlan, SubscriptionStates.Canceled, $"{Shopper.Reference}:eshop-pro");

        var service = BuildService(client);
        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadyExisted);

        // The reference is ordinal-suffixed so it does not collide with the subscription that was left.
        Assert.Equal($"{Shopper.Reference}:eshop-pro:2", client.CreateSubscriptionRequests.Single().Subscription.Reference);
    }

    [Theory]
    [InlineData(SubscriptionStates.Active)]
    [InlineData(SubscriptionStates.Trialing)]
    [InlineData(SubscriptionStates.PastDue)]
    [InlineData(SubscriptionStates.OnHold)]
    public async Task TreatsEveryLiveStateAsAlreadySubscribed(string state)
    {
        var client = new FakeMaxioApiClient(ProPlan);
        var customer = client.SeedCustomer(Shopper.Reference);
        client.SeedSubscription(customer.Id, ProPlan, state);

        var result = await BuildService(client).SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(result.AlreadyExisted);
    }

    [Fact]
    public async Task RequestsInvoiceStyleCollectionBecauseNoPaymentInstrumentIsCaptured()
    {
        var client = new FakeMaxioApiClient(ProPlan);
        await BuildService(client).SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(CollectionMethods.Remittance, client.CreateSubscriptionRequests.Single().Subscription.PaymentCollectionMethod);
    }

    [Fact]
    public async Task FallsBackToLegacyInvoiceCollectionOnStatementsSites()
    {
        var client = new FakeMaxioApiClient(ProPlan);
        client.Site = client.Site with { RelationshipInvoicingEnabled = false };

        await BuildService(client).SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(CollectionMethods.Invoice, client.CreateSubscriptionRequests.Single().Subscription.PaymentCollectionMethod);
    }

    [Fact]
    public async Task RejectsAPlanThatIsNotPublishedInTheConfiguredFamily()
    {
        var service = BuildService(new FakeMaxioApiClient(ProPlan));

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(Shopper, "not-a-plan"));
    }

    [Fact]
    public async Task RejectsAnArchivedPlan()
    {
        var service = BuildService(new FakeMaxioApiClient(ProPlan, ArchivedPlan));

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(Shopper, "retired"));
    }

    [Fact]
    public async Task RejectsAPlanThatDemandsAStoredPaymentMethodBeforeTouchingTheBillingSystem()
    {
        var client = new FakeMaxioApiClient(ProPlan, CardOnlyPlan);
        var service = BuildService(client);

        await Assert.ThrowsAsync<PaymentMethodRequiredException>(() => service.SubscribeAsync(Shopper, "card-only"));

        Assert.Equal(0, client.CreateCustomerCallCount);
        Assert.Equal(0, client.CreateSubscriptionCallCount);
    }

    [Fact]
    public async Task ListsPlansWithoutArchivedProductsCheapestFirst()
    {
        var cheaper = ProPlan with { Id = 4, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2_900 };
        var service = BuildService(new FakeMaxioApiClient(ProPlan, ArchivedPlan, cheaper));

        var plans = await service.ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(plan => plan.Handle));
        Assert.Equal("USD", plans[0].Currency);
        Assert.Equal(29m, plans[0].Price);
    }

    [Fact]
    public async Task ReportsNoSubscriptionsForAShopperWhoNeverSubscribed()
    {
        var service = BuildService(new FakeMaxioApiClient(ProPlan));

        Assert.Empty(await service.ListSubscriptionsAsync(Shopper));
    }

    [Fact]
    public async Task ListsTheShoppersSubscriptionsNewestFirst()
    {
        var client = new FakeMaxioApiClient(ProPlan);
        var service = BuildService(client);

        await service.SubscribeAsync(Shopper, "eshop-pro");
        var subscriptions = await service.ListSubscriptionsAsync(Shopper);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.True(subscription.IsLive);
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
