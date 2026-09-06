using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Services;
using Microsoft.eShopWeb.MaxioBillingTests.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests.Services;

public class MaxioSubscriptionServiceTests
{
    private static readonly SubscriberProfile Shopper =
        new("user-1", "demouser@microsoft.com", "Demo", "Shopper");

    [Fact]
    public async Task Creates_the_customer_and_the_subscription_on_a_first_subscribe()
    {
        var (service, fake) = Build();

        var result = await service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro"));

        Assert.True(result.Created);
        Assert.True(result.CustomerCreated);
        Assert.Equal(SubscriptionState.Active, result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.NotNull(result.Subscription.NextBillingAt);
        Assert.Single(fake.Subscriptions);
    }

    [Fact]
    public async Task Repeating_a_subscribe_returns_the_existing_subscription()
    {
        var (service, fake) = Build();

        var first = await service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro"));
        var second = await service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro"));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subscription.Id, second.Subscription.Id);
        Assert.Equal(1, fake.CreateCustomerCalls);
        Assert.Single(fake.Subscriptions);
    }

    [Fact]
    public async Task Concurrent_subscribes_enroll_the_shopper_exactly_once()
    {
        var (service, fake) = Build();

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro"))));

        Assert.Single(fake.Subscriptions);
        Assert.Equal(1, results.Count(result => result.Created));
        Assert.Single(results.Select(result => result.Subscription.Id).Distinct());
    }

    [Fact]
    public async Task A_provider_side_race_on_the_reference_resolves_to_the_existing_subscription()
    {
        var (service, fake) = Build();

        var customerReference = MaxioReferences.ForCustomer("eshoponweb", Shopper.Email);
        var customer = fake.SeedCustomer(customerReference);

        // Simulate another instance claiming the reference in the instant between our check and our
        // create, which is exactly what the provider-side uniqueness rule is there to catch.
        fake.BeforeCreateSubscription = () =>
        {
            fake.BeforeCreateSubscription = null;
            fake.SeedSubscription(
                customer,
                "eshop-pro",
                "active",
                MaxioReferences.ForSubscription(customerReference, "eshop-pro"));

            return Task.CompletedTask;
        };

        var result = await service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro"));

        Assert.False(result.Created);
        Assert.Single(fake.Subscriptions);
    }

    [Fact]
    public async Task An_existing_billing_customer_is_reused_rather_than_duplicated()
    {
        var (service, fake) = Build();
        fake.SeedCustomer(MaxioReferences.ForCustomer("eshoponweb", Shopper.Email));

        var result = await service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro"));

        Assert.True(result.Created);
        Assert.False(result.CustomerCreated);
        Assert.Equal(0, fake.CreateCustomerCalls);
    }

    [Fact]
    public async Task A_provider_side_race_on_the_customer_reference_adopts_the_existing_customer()
    {
        var (service, fake) = Build();
        var customerReference = MaxioReferences.ForCustomer("eshoponweb", Shopper.Email);

        // The customer appears between our lookup and our create, so the provider refuses the
        // duplicate. That refusal must resolve to the record that won, not bubble up as an error.
        fake.BeforeCreateCustomer = () =>
        {
            fake.BeforeCreateCustomer = null;
            fake.SeedCustomer(customerReference);
            return Task.CompletedTask;
        };

        var result = await service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro"));

        Assert.True(result.Created);
        Assert.False(result.CustomerCreated);
        Assert.Equal(1, fake.CreateCustomerCalls);
    }

    [Fact]
    public async Task A_shopper_who_cancelled_can_subscribe_to_the_same_plan_again()
    {
        var (service, fake) = Build();
        var customerReference = MaxioReferences.ForCustomer("eshoponweb", Shopper.Email);
        var customer = fake.SeedCustomer(customerReference);

        var spentReference = MaxioReferences.ForSubscription(customerReference, "eshop-pro");
        fake.SeedSubscription(customer, "eshop-pro", "canceled", spentReference);

        var result = await service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro"));

        Assert.True(result.Created);
        Assert.Equal(MaxioReferences.WithSequence(spentReference, 2), result.Subscription.Reference);
        Assert.Equal(2, fake.Subscriptions.Count);
    }

    [Fact]
    public async Task A_past_due_subscription_still_blocks_a_duplicate_signup()
    {
        var (service, fake) = Build();
        var customerReference = MaxioReferences.ForCustomer("eshoponweb", Shopper.Email);
        var customer = fake.SeedCustomer(customerReference);

        fake.SeedSubscription(customer, "eshop-pro", "past_due", "some-other-reference");

        var result = await service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro"));

        Assert.False(result.Created);
        Assert.Single(fake.Subscriptions);
    }

    [Fact]
    public async Task An_idempotency_key_replays_to_the_same_subscription_even_after_cancellation()
    {
        var (service, fake) = Build();
        var customerReference = MaxioReferences.ForCustomer("eshoponweb", Shopper.Email);
        var customer = fake.SeedCustomer(customerReference);

        var keyedReference = MaxioReferences.ForSubscription(customerReference, "eshop-pro", "order-4711");
        var seeded = fake.SeedSubscription(customer, "eshop-pro", "canceled", keyedReference);

        var result = await service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro", "order-4711"));

        Assert.False(result.Created);
        Assert.Equal(seeded.Id, result.Subscription.Id);
        Assert.Single(fake.Subscriptions);
    }

    [Fact]
    public async Task An_unknown_plan_is_rejected_before_anything_is_created()
    {
        var (service, fake) = Build();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(new SubscribeCommand(Shopper, "no-such-plan")));

        Assert.Equal(0, fake.CreateCustomerCalls);
        Assert.Empty(fake.Subscriptions);
    }

    [Fact]
    public async Task A_shopper_with_no_billing_customer_has_no_subscriptions()
    {
        var (service, _) = Build();

        Assert.Empty(await service.ListSubscriptionsAsync(Shopper));
    }

    [Fact]
    public async Task Subscriptions_are_listed_newest_first()
    {
        var (service, fake) = Build();
        var customerReference = MaxioReferences.ForCustomer("eshoponweb", Shopper.Email);
        var customer = fake.SeedCustomer(customerReference);

        // Seeded subscriptions are dated a day earlier than anything created during the test.
        var older = fake.SeedSubscription(customer, "eshop-pro", "canceled", "older-reference");
        var newer = await service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro"));

        var listed = await service.ListSubscriptionsAsync(Shopper);

        Assert.Equal(2, listed.Count);
        Assert.Equal(newer.Subscription.Id, listed[0].Id);
        Assert.Equal(older.Id, listed[1].Id);
        Assert.True(listed[0].State.IsOccupied());
        Assert.False(listed[1].State.IsOccupied());
    }

    [Fact]
    public async Task Billing_that_is_not_configured_fails_loudly_rather_than_silently()
    {
        var (service, _) = Build(new MaxioOptions { ApiKey = null, Subdomain = null, ProductFamilyHandle = null });

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => service.SubscribeAsync(new SubscribeCommand(Shopper, "eshop-pro")));
    }

    private static (MaxioSubscriptionService Service, FakeMaxioApiClient Client) Build(MaxioOptions? options = null)
    {
        options ??= new MaxioOptions
        {
            ApiKey = "k",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        };

        var fake = new FakeMaxioApiClient();
        var monitor = new StaticOptionsMonitor<MaxioOptions>(options);

        ISubscriptionPlanCatalog catalog = new MaxioSubscriptionPlanCatalog(
            fake,
            new MemoryCache(new MemoryCacheOptions()),
            monitor,
            NullLogger<MaxioSubscriptionPlanCatalog>.Instance);

        var service = new MaxioSubscriptionService(
            fake,
            catalog,
            new KeyedAsyncLock(),
            monitor,
            NullLogger<MaxioSubscriptionService>.Instance);

        return (service, fake);
    }
}
