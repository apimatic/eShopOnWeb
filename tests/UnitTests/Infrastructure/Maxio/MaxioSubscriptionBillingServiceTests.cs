using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string ProPlan = "eshop-pro";
    private const string BasicPlan = "basic-plan";

    private readonly FakeMaxioApiClient _maxio = new();
    private readonly SubscriberIdentity _shopper =
        new("demouser@microsoft.com", "demouser@microsoft.com", "Demo", "Shopper");

    public MaxioSubscriptionBillingServiceTests()
    {
        _maxio.AddProduct(ProPlan, "Pro Plan", 29_900, FamilyHandle);
        _maxio.AddProduct(BasicPlan, "Basic Plan", 2_900, FamilyHandle);
    }

    [Fact]
    public async Task ListPlansReturnsTheConfiguredFamilyPricedInMajorUnits()
    {
        var plans = await CreateService().ListPlansAsync();

        Assert.Collection(
            plans,
            basic =>
            {
                Assert.Equal(BasicPlan, basic.Handle);
                Assert.Equal(29m, basic.Price);
                Assert.Equal("USD", basic.Currency);
            },
            pro =>
            {
                Assert.Equal(ProPlan, pro.Handle);
                Assert.Equal(299m, pro.Price);
            });
    }

    [Fact]
    public async Task ListPlansExcludesArchivedProducts()
    {
        _maxio.AddProduct("retired-plan", "Retired", 100, FamilyHandle, archivedAt: DateTimeOffset.UtcNow);

        var plans = await CreateService().ListPlansAsync();

        Assert.DoesNotContain(plans, p => p.Handle == "retired-plan");
    }

    [Fact]
    public async Task ListPlansIgnoresProductsOutsideTheConfiguredFamily()
    {
        _maxio.AddProduct("other-plan", "Other", 100, "some-other-family");

        var plans = await CreateService().ListPlansAsync();

        Assert.DoesNotContain(plans, p => p.Handle == "other-plan");
    }

    [Fact]
    public async Task SubscribeCreatesTheBillingCustomerOnFirstUse()
    {
        var result = await CreateService().SubscribeAsync(_shopper, ProPlan);

        Assert.True(result.Created);
        Assert.Equal(ProPlan, result.Subscription.PlanHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.IsLive);

        var customer = Assert.Single(_maxio.Customers);
        Assert.Equal(_shopper.BillingReference, customer.Reference);
        Assert.Equal(_shopper.Email, customer.Email);
    }

    [Fact]
    public async Task SubscribeReusesAnExistingBillingCustomer()
    {
        _maxio.AddCustomer(_shopper.BillingReference);

        await CreateService().SubscribeAsync(_shopper, ProPlan);

        Assert.Single(_maxio.Customers);
        Assert.Equal(0, _maxio.CreateCustomerCalls);
    }

    [Fact]
    public async Task SubscribeAdoptsTheCustomerCreatedByAConcurrentCaller()
    {
        // Lookup says "absent", then someone else creates it before our own create lands.
        _maxio.BeforeCreateCustomer = attributes => _maxio.AddCustomer(attributes.Reference!);

        var result = await CreateService().SubscribeAsync(_shopper, ProPlan);

        Assert.True(result.Created);
        Assert.Single(_maxio.Customers);
    }

    [Fact]
    public async Task SubscribingTwiceReturnsTheSameSubscription()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(_shopper, ProPlan);
        var second = await service.SubscribeAsync(_shopper, ProPlan);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Single(_maxio.Subscriptions);
    }

    [Fact]
    public async Task SubscribeAdoptsASubscriptionCreatedByAConcurrentCaller()
    {
        // The pre-check sees nothing, but another writer claims the reference first. Only Maxio's
        // uniqueness constraint stands between the shopper and a duplicate subscription here.
        var customer = _maxio.AddCustomer(_shopper.BillingReference);
        MaxioSubscriptionAttributes? attempted = null;

        _maxio.BeforeCreateSubscription = attributes =>
        {
            if (attempted is not null)
            {
                return;
            }

            attempted = attributes;
            _maxio.AddSubscription(customer, ProPlan, "active", attributes.Reference!);
        };

        var result = await CreateService().SubscribeAsync(_shopper, ProPlan);

        Assert.False(result.Created);
        Assert.Single(_maxio.Subscriptions);
        Assert.Equal(attempted!.Reference, result.Subscription.Reference);
    }

    [Fact]
    public async Task SubscribeAfterCancellationCreatesAFreshSubscription()
    {
        var customer = _maxio.AddCustomer(_shopper.BillingReference);
        var cancelledReference = BillingReferences.ForSubscription(_shopper.BillingReference, ProPlan, 0);
        _maxio.AddSubscription(customer, ProPlan, "canceled", cancelledReference);

        var result = await CreateService().SubscribeAsync(_shopper, ProPlan);

        Assert.True(result.Created);
        Assert.Equal(
            BillingReferences.ForSubscription(_shopper.BillingReference, ProPlan, 1),
            result.Subscription.Reference);
        Assert.Equal(2, _maxio.Subscriptions.Count);
    }

    [Fact]
    public async Task SubscribeRecomputesWhenTheDerivedReferenceBelongsToAnEndedSubscription()
    {
        // A stale ordinal — the reference we derived is taken by a subscription that has since
        // ended — must lead to a new subscription, not to adopting a dead one.
        var customer = _maxio.AddCustomer(_shopper.BillingReference);
        var contested = BillingReferences.ForSubscription(_shopper.BillingReference, ProPlan, 0);
        var raced = false;

        _maxio.BeforeCreateSubscription = attributes =>
        {
            if (raced)
            {
                return;
            }

            raced = true;
            _maxio.AddSubscription(customer, ProPlan, "canceled", contested);
        };

        var result = await CreateService().SubscribeAsync(_shopper, ProPlan);

        Assert.True(result.Created);
        Assert.Equal(BillingReferences.ForSubscription(_shopper.BillingReference, ProPlan, 1), result.Subscription.Reference);
        Assert.True(result.Subscription.IsLive);
    }

    [Fact]
    public async Task SubscribeTreatsAPastDueSubscriptionAsStillHeld()
    {
        var customer = _maxio.AddCustomer(_shopper.BillingReference);
        _maxio.AddSubscription(customer, ProPlan, "past_due", "existing-past-due");

        var result = await CreateService().SubscribeAsync(_shopper, ProPlan);

        Assert.False(result.Created);
        Assert.Equal("past_due", result.Subscription.State);
        Assert.Single(_maxio.Subscriptions);
    }

    [Fact]
    public async Task SubscribeWithTheSameIdempotencyKeyNeverCreatesTwice()
    {
        var service = CreateService();

        var first = await service.SubscribeAsync(_shopper, ProPlan, "checkout-42");
        var second = await service.SubscribeAsync(_shopper, ProPlan, "checkout-42");

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Single(_maxio.Subscriptions);
    }

    [Fact]
    public async Task SubscribeWithAnIdempotencyKeyReplaysAnEndedSubscription()
    {
        // The key identifies one request, so replaying it must return that request's outcome even
        // after the subscription has been cancelled — not quietly start billing again.
        var customer = _maxio.AddCustomer(_shopper.BillingReference);
        var reference = BillingReferences.ForSubscription(_shopper.BillingReference, ProPlan, "checkout-42");
        _maxio.AddSubscription(customer, ProPlan, "canceled", reference);

        var result = await CreateService().SubscribeAsync(_shopper, ProPlan, "checkout-42");

        Assert.False(result.Created);
        Assert.Equal("canceled", result.Subscription.State);
        Assert.Single(_maxio.Subscriptions);
    }

    [Fact]
    public async Task ConcurrentSubscribeAttemptsProduceOneSubscription()
    {
        var service = CreateService();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => service.SubscribeAsync(_shopper, ProPlan)));

        Assert.Single(_maxio.Subscriptions);
        Assert.Single(_maxio.Customers);
        Assert.Single(results, r => r.Created);
        Assert.All(results, r => Assert.Equal(_maxio.Subscriptions[0].Id, r.Subscription.Id));
    }

    [Fact]
    public async Task SubscribeRejectsAPlanOutsideTheConfiguredFamily()
    {
        _maxio.AddProduct("other-plan", "Other", 100, "some-other-family");

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(_shopper, "other-plan"));

        Assert.Empty(_maxio.Subscriptions);
    }

    [Fact]
    public async Task SubscribeRejectsAnUnknownPlan()
    {
        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(_shopper, "no-such-plan"));
    }

    [Fact]
    public async Task ListSubscriptionsReturnsNothingForAShopperWhoNeverSubscribed()
    {
        var subscriptions = await CreateService().ListSubscriptionsAsync(_shopper);

        Assert.Empty(subscriptions);
        Assert.Empty(_maxio.Customers);
    }

    [Fact]
    public async Task ListSubscriptionsReturnsMostRecentFirstAndFlagsLiveness()
    {
        var service = CreateService();
        await service.SubscribeAsync(_shopper, ProPlan);
        _maxio.Subscriptions[0].State = "canceled";
        await service.SubscribeAsync(_shopper, BasicPlan);

        var subscriptions = await service.ListSubscriptionsAsync(_shopper);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(BasicPlan, subscriptions[0].PlanHandle);
        Assert.True(subscriptions[0].IsLive);
        Assert.False(subscriptions[1].IsLive);
    }

    [Fact]
    public async Task OperationsFailLoudlyWhenBillingIsNotConfigured()
    {
        var service = CreateService(new MaxioSettings { ApiKey = null, Subdomain = null, ProductFamilyHandle = null });

        var listPlans = await Assert.ThrowsAsync<BillingNotConfiguredException>(() => service.ListPlansAsync());
        await Assert.ThrowsAsync<BillingNotConfiguredException>(() => service.SubscribeAsync(_shopper, ProPlan));
        await Assert.ThrowsAsync<BillingNotConfiguredException>(() => service.ListSubscriptionsAsync(_shopper));

        // The message has to tell an operator what to supply without ever naming a value.
        Assert.Contains("Maxio:ApiKey", listPlans.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", listPlans.Message);
    }

    [Fact]
    public async Task SubscribeUsesTheConfiguredPaymentCollectionMethod()
    {
        var service = CreateService(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = FamilyHandle,
            PaymentCollectionMethod = "automatic"
        });

        var result = await service.SubscribeAsync(_shopper, ProPlan);

        Assert.Equal("automatic", result.Subscription.PaymentCollectionMethod);
    }

    private MaxioSubscriptionBillingService CreateService(MaxioSettings? settings = null) =>
        new(
            _maxio,
            new StaticOptionsMonitor<MaxioSettings>(settings ?? new MaxioSettings
            {
                ApiKey = "test-key",
                Subdomain = "test-site",
                ProductFamilyHandle = FamilyHandle
            }),
            new SubscriberGate(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
}

internal class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
