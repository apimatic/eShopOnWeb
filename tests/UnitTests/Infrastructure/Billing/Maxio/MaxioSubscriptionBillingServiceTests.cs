using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string ProPlan = "eshop-pro";
    private const string BasicPlan = "basic-plan";

    private static readonly Subscriber DemoUser =
        Subscriber.ForUser("demouser@microsoft.com", "demouser@microsoft.com");

    private readonly FakeMaxioApiClient _api = new();

    public MaxioSubscriptionBillingServiceTests()
    {
        _api.Products.Add(Product(ProPlan, "Pro Plan", 29900));
        _api.Products.Add(Product(BasicPlan, "Basic Plan", 2900));
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsActivePlansCheapestFirst()
    {
        var service = CreateService();

        var plans = await service.ListPlansAsync();

        Assert.Collection(plans,
            plan => Assert.Equal(BasicPlan, plan.Handle),
            plan => Assert.Equal(ProPlan, plan.Handle));
        Assert.Equal(299m, plans[1].Price);
        Assert.Equal("USD", plans[1].Currency);
    }

    [Fact]
    public async Task ListPlansAsync_ExcludesArchivedPlans()
    {
        _api.Products.Add(Product("retired-plan", "Retired", 100) with
        {
            ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        var service = CreateService();

        var plans = await service.ListPlansAsync();

        Assert.DoesNotContain(plans, p => p.Handle == "retired-plan");
    }

    [Fact]
    public async Task SubscribeAsync_CreatesTheCustomerAndTheSubscription()
    {
        var service = CreateService();

        var enrollment = await service.SubscribeAsync(DemoUser, ProPlan);

        Assert.False(enrollment.AlreadyExisted);
        Assert.Equal("active", enrollment.Subscription.State);
        Assert.Equal(ProPlan, enrollment.Subscription.PlanHandle);
        Assert.Equal(299m, enrollment.Subscription.Price);
        Assert.NotNull(enrollment.Subscription.NextBillingAt);
        Assert.Single(_api.Customers);
        Assert.Single(_api.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenCalledTwice()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(DemoUser, ProPlan);
        var second = await service.SubscribeAsync(DemoUser, ProPlan);

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Single(_api.Subscriptions);
        Assert.Equal(1, _api.CreateCustomerCalls);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsTheWinnerWhenAnotherCallerCreatesTheSameSubscriptionFirst()
    {
        // Reproduce the double click that gets past the pre-check: a competing caller creates the
        // subscription between this caller's "already subscribed?" read and its create.
        var service = CreateService();
        var competitor = CreateService();

        _api.BeforeCreateSubscription = async () =>
        {
            _api.BeforeCreateSubscription = null;
            await competitor.SubscribeAsync(DemoUser, ProPlan);
        };

        var enrollment = await service.SubscribeAsync(DemoUser, ProPlan);

        Assert.True(enrollment.AlreadyExisted);
        Assert.Single(_api.Subscriptions);
        Assert.Equal(enrollment.Subscription.Id, _api.Subscriptions[0].Id);
    }

    [Fact]
    public async Task SubscribeAsync_CollapsesConcurrentRequestsFromTheSameShopper()
    {
        var service = CreateService();

        var results = await Task.WhenAll(
            Task.Run(() => service.SubscribeAsync(DemoUser, ProPlan)),
            Task.Run(() => service.SubscribeAsync(DemoUser, ProPlan)),
            Task.Run(() => service.SubscribeAsync(DemoUser, ProPlan)));

        Assert.Single(_api.Subscriptions);
        Assert.Single(_api.Customers);
        Assert.All(results, r => Assert.Equal(_api.Subscriptions[0].Id, r.Subscription.Id));
        Assert.Equal(1, results.Count(r => !r.AlreadyExisted));
    }

    [Fact]
    public async Task SubscribeAsync_StartsAFreshSubscriptionAfterTheEarlierOneEnded()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(DemoUser, ProPlan);
        Cancel(first.Subscription.Id);

        var second = await service.SubscribeAsync(DemoUser, ProPlan);

        Assert.False(second.AlreadyExisted);
        Assert.NotEqual(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(2, _api.Subscriptions.Count);
        Assert.EndsWith("--eshop-pro--2", second.Subscription.Reference);
    }

    [Fact]
    public async Task SubscribeAsync_AllowsASecondSubscriptionOnADifferentPlan()
    {
        var service = CreateService();

        await service.SubscribeAsync(DemoUser, ProPlan);
        var basic = await service.SubscribeAsync(DemoUser, BasicPlan);

        Assert.False(basic.AlreadyExisted);
        Assert.Equal(2, _api.Subscriptions.Count);
        Assert.Single(_api.Customers);
    }

    [Fact]
    public async Task SubscribeAsync_ReusesTheCustomerCreatedByACompetingCaller()
    {
        var service = CreateService();
        _api.Customers.Add(new MaxioCustomer
        {
            Id = 42,
            Reference = MaxioReference.ForCustomer("eshoponweb", DemoUser.UserKey),
            Email = DemoUser.Email
        });

        await service.SubscribeAsync(DemoUser, ProPlan);

        Assert.Equal(0, _api.CreateCustomerCalls);
        Assert.Equal(42, _api.Subscriptions[0].Customer!.Id);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAnUnknownPlan()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(DemoUser, "not-a-plan"));

        Assert.Equal("not-a-plan", exception.PlanHandle);
        Assert.Empty(_api.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_RefusesPlansThatNeedAStoredPaymentMethod()
    {
        _api.Products.Add(Product("card-required", "Card Required", 1000) with { RequireCreditCard = true });
        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionNotAllowedException>(
            () => service.SubscribeAsync(DemoUser, "card-required"));

        Assert.Empty(_api.Subscriptions);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsNothingForAShopperWhoNeverSubscribed()
    {
        var service = CreateService();

        var subscriptions = await service.ListSubscriptionsAsync(DemoUser);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsTheShoppersOwnSubscriptionsOnly()
    {
        var service = CreateService();
        var otherUser = Subscriber.ForUser("someone@else.com", "someone@else.com");

        await service.SubscribeAsync(DemoUser, ProPlan);
        await service.SubscribeAsync(otherUser, BasicPlan);

        var subscriptions = await service.ListSubscriptionsAsync(DemoUser);

        Assert.Single(subscriptions);
        Assert.Equal(ProPlan, subscriptions[0].PlanHandle);
        Assert.True(subscriptions[0].IsLive);
    }

    private ISubscriptionBillingService CreateService() => new MaxioSubscriptionBillingService(
        _api,
        Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle,

            // Each service instance gets its own cache so a test that mutates the catalog is not
            // served a stale plan list.
            PlanCacheSeconds = 0
        }),
        new MemoryCache(new MemoryCacheOptions()),
        new KeyedAsyncLock(),
        NullLogger<MaxioSubscriptionBillingService>.Instance);

    private void Cancel(long subscriptionId)
    {
        var index = _api.Subscriptions.FindIndex(s => s.Id == subscriptionId);
        _api.Subscriptions[index] = _api.Subscriptions[index] with
        {
            State = "canceled",
            CanceledAt = DateTimeOffset.UtcNow
        };
    }

    private static MaxioProduct Product(string handle, string name, int priceInCents) => new()
    {
        Id = handle.GetHashCode(),
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Id = 1, Handle = FamilyHandle, Name = "eShop Subscribe" }
    };
}
