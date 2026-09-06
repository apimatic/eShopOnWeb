using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string ProductFamilyHandle = "demo-family";
    private const string ProPlanHandle = "pro-plan";
    private const string BasicPlanHandle = "starter-plan";
    private const long ProductFamilyId = 3026731;

    private static readonly Subscriber Shopper = new("demouser@microsoft.com");

    private readonly FakeMaxioApiClient _client = new();

    public MaxioSubscriptionBillingServiceTests()
    {
        _client.SeedFamily(
            ProductFamilyHandle,
            ProductFamilyId,
            NewProduct(ProPlanHandle, "Pro Plan", 29900),
            NewProduct(BasicPlanHandle, "Basic Plan", 2900));
    }

    [Fact]
    public async Task GetPlansAsync_ReturnsTheConfiguredFamilyOrderedByPrice()
    {
        var plans = await CreateService().GetPlansAsync();

        Assert.Equal(new[] { BasicPlanHandle, ProPlanHandle }, plans.Select(p => p.Handle));
        Assert.Equal(299.00m, plans.Single(p => p.Handle == ProPlanHandle).Price);
        Assert.Equal("USD", plans.Single(p => p.Handle == ProPlanHandle).Currency);
    }

    [Fact]
    public async Task GetPlansAsync_ExcludesArchivedPlans()
    {
        _client.ProductsByFamilyId[ProductFamilyId]
            .Single(p => p.Handle == BasicPlanHandle)
            .ArchivedAt = DateTimeOffset.UtcNow;

        var plans = await CreateService().GetPlansAsync();

        Assert.Equal(new[] { ProPlanHandle }, plans.Select(p => p.Handle));
    }

    [Fact]
    public async Task GetPlansAsync_ResolvesTheFamilyByHandleNotByStaleId()
    {
        // The seeded numeric id is deliberately not the one anybody configured; the handle is.
        var plans = await CreateService().GetPlansAsync();

        Assert.NotEmpty(plans);
        Assert.All(plans, plan => Assert.Equal(ProductFamilyHandle, plan.ProductFamilyHandle));
    }

    [Fact]
    public async Task SubscribeAsync_CreatesTheCustomerAndTheSubscription()
    {
        var result = await CreateService().SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(SubscriptionState.Active, result.Subscription.State);
        Assert.Equal(ProPlanHandle, result.Subscription.PlanHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Single(_client.Customers);
        Assert.Single(_client.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentAcrossRepeatedRequests()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));
        var second = await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));

        Assert.False(first.AlreadySubscribed);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Single(_client.Customers);
        Assert.Single(_client.Subscriptions);
        Assert.Equal(1, _client.SubscriptionCreateCount);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentAcrossSeparateServiceInstances()
    {
        // Separate instances share no cache, which is what a second application instance looks like.
        var first = await CreateService().SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));
        var second = await CreateService().SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));

        Assert.True(second.AlreadySubscribed);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Single(_client.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_CollapsesOntoTheWinnerWhenAConcurrentRequestGetsThereFirst()
    {
        var service = CreateService();

        // Simulate the race: the other request's subscription lands between our "does one exist?"
        // check and our create, so the provider rejects ours on reference uniqueness.
        _client.BeforeCreateSubscription = create =>
        {
            _client.BeforeCreateSubscription = null;

            _client.Subscriptions.Add(_client.NewSubscription(
                reference: create.Reference,
                state: "active",
                product: _client.ProductsByFamilyId[ProductFamilyId].Single(p => p.Handle == create.ProductHandle),
                customer: _client.Customers.Single(c => c.Id == create.CustomerId)));
        };

        var result = await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));

        Assert.True(result.AlreadySubscribed);
        Assert.Single(_client.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_AdoptsACustomerCreatedConcurrently()
    {
        var service = CreateService();
        var customerReference = MaxioReference.ForCustomer(Shopper.UserName);

        // A concurrent request creates the customer first; ours must adopt it, not duplicate it.
        _client.Customers.Add(new MaxioCustomer { Id = 777, Reference = customerReference, Email = Shopper.Email });

        var result = await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));

        Assert.Equal(777, result.Subscription.CustomerId);
        Assert.Single(_client.Customers);
        Assert.Equal(0, _client.CustomerCreateCount);
    }

    [Fact]
    public async Task SubscribeAsync_AllowsDifferentPlansForTheSameSubscriber()
    {
        var service = CreateService();

        await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));
        var second = await service.SubscribeAsync(new SubscribeRequest(Shopper, BasicPlanHandle));

        Assert.False(second.AlreadySubscribed);
        Assert.Equal(2, _client.Subscriptions.Count);
        Assert.Single(_client.Customers);
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotBlockAnotherSubscriberFromTheSamePlan()
    {
        var service = CreateService();

        await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));
        var other = await service.SubscribeAsync(new SubscribeRequest(new Subscriber("admin@microsoft.com"), ProPlanHandle));

        Assert.False(other.AlreadySubscribed);
        Assert.Equal(2, _client.Customers.Count);
        Assert.Equal(2, _client.Subscriptions.Count);
    }

    [Fact]
    public async Task SubscribeAsync_TreatsAnExplicitIdempotencyKeyAsADistinctIntent()
    {
        var service = CreateService();

        await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));
        var second = await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle, "second-seat"));

        Assert.False(second.AlreadySubscribed);
        Assert.Equal(2, _client.Subscriptions.Count);
    }

    [Fact]
    public async Task SubscribeAsync_RepeatsAreStillIdempotentUnderAnExplicitKey()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle, "second-seat"));
        var second = await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle, "second-seat"));

        Assert.True(second.AlreadySubscribed);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Single(_client.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_ResubscribesAfterCancellationWhenGivenAFreshKey()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));
        Cancel(first.Subscription.Id);

        var second = await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle, "resubscribe-2026-09"));

        Assert.False(second.AlreadySubscribed);
        Assert.NotEqual(first.Subscription.Id, second.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_RefusesToReuseAKeyConsumedByACancelledSubscription()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));
        Cancel(first.Subscription.Id);

        // The default key is derived from the plan, so it is already spent. Answer clearly rather
        // than silently returning the dead subscription or quietly billing a new one.
        var exception = await Assert.ThrowsAsync<SubscriptionConflictException>(
            () => service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle)));

        Assert.Contains("idempotencyKey", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_client.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAnUnknownPlanHandle()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(new SubscribeRequest(Shopper, "not-a-plan")));

        Assert.Empty(_client.Customers);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAPlanFromOutsideTheConfiguredFamily()
    {
        _client.SeedFamily("some-other-family", 99, NewProduct("unrelated-plan", "Unrelated", 100));

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(new SubscribeRequest(Shopper, "unrelated-plan")));
    }

    [Fact]
    public async Task SubscribeAsync_RejectsAPlanThatNeedsAStoredPaymentMethod()
    {
        _client.ProductsByFamilyId[ProductFamilyId]
            .Single(p => p.Handle == ProPlanHandle)
            .RequireCreditCard = true;

        var exception = await Assert.ThrowsAsync<BillingRequestRejectedException>(
            () => CreateService().SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle)));

        Assert.Contains("payment method", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_client.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAsync_UsesTheConfiguredPaymentCollectionMethod()
    {
        var settings = ConfiguredSettings();
        settings.PaymentCollectionMethod = "remittance";

        await CreateService(settings).SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));

        Assert.Equal("remittance", _client.Subscriptions.Single().PaymentCollectionMethod);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmptyForASubscriberWithNoBillingCustomer()
    {
        Assert.Empty(await CreateService().GetSubscriptionsAsync(Shopper));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsOnlyTheSubscriberOwnSubscriptions()
    {
        var service = CreateService();
        var other = new Subscriber("admin@microsoft.com");

        await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));
        await service.SubscribeAsync(new SubscribeRequest(other, BasicPlanHandle));

        var mine = await service.GetSubscriptionsAsync(Shopper);

        Assert.Equal(new[] { ProPlanHandle }, mine.Select(s => s.PlanHandle));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_IncludesCancelledSubscriptions()
    {
        var service = CreateService();

        var created = await service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle));
        Cancel(created.Subscription.Id);

        var mine = await service.GetSubscriptionsAsync(Shopper);

        Assert.Equal(SubscriptionState.Canceled, Assert.Single(mine).State);
    }

    [Fact]
    public async Task EveryOperationReportsMissingConfigurationInsteadOfCallingTheProvider()
    {
        var service = CreateService(new MaxioSettings());

        await Assert.ThrowsAsync<BillingNotConfiguredException>(() => service.GetPlansAsync());
        await Assert.ThrowsAsync<BillingNotConfiguredException>(() => service.GetSubscriptionsAsync(Shopper));
        await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => service.SubscribeAsync(new SubscribeRequest(Shopper, ProPlanHandle)));
    }

    [Fact]
    public async Task BillingNotConfigured_NamesTheMissingKeysWithoutRevealingValues()
    {
        var exception = await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => CreateService(new MaxioSettings()).GetPlansAsync());

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }

    private void Cancel(long subscriptionId)
    {
        var index = _client.Subscriptions.FindIndex(s => s.Id == subscriptionId);
        _client.Subscriptions[index].State = "canceled";
        _client.Subscriptions[index].CanceledAt = DateTimeOffset.UtcNow;
    }

    private MaxioSubscriptionBillingService CreateService(MaxioSettings? settings = null) =>
        new(_client,
            new StaticOptionsMonitor<MaxioSettings>(settings ?? ConfiguredSettings()),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

    private static MaxioSettings ConfiguredSettings() => new()
    {
        ApiKey = "not-a-real-key",
        Subdomain = "test-site",
        ProductFamilyHandle = ProductFamilyHandle
    };

    private static MaxioProduct NewProduct(string handle, string name, long priceInCents) => new()
    {
        Id = Random.Shared.NextInt64(1_000_000, 9_999_999),
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        RequireCreditCard = false
    };
}
