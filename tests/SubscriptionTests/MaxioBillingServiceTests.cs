using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.SubscriptionTests;

public class MaxioBillingServiceTests
{
    private static BillingUser Shopper => new("shopper@example.com", "shopper@example.com", "shopper", "eShopOnWeb");

    [Fact]
    public async Task ListPlansAsync_maps_products_in_the_configured_family()
    {
        var fake = new FakeMaxio();
        var service = MaxioTestHarness.Build(new StubHandler(fake.Respond));

        var plans = (await service.ListPlansAsync()).ToList();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_creates_a_subscription_when_none_exists()
    {
        var fake = new FakeMaxio { CustomerFound = true, HasActiveProSubscription = false };
        var handler = new StubHandler(fake.Respond);
        var service = MaxioTestHarness.Build(handler);

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(1, fake.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribeAsync_is_idempotent_when_an_active_subscription_exists()
    {
        var fake = new FakeMaxio { CustomerFound = true, HasActiveProSubscription = true };
        var service = MaxioTestHarness.Build(new StubHandler(fake.Respond));

        var result = await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(0, fake.CreateSubscriptionCalls); // No duplicate created.
    }

    [Fact]
    public async Task SubscribeAsync_creates_the_customer_when_the_reference_is_unknown()
    {
        var fake = new FakeMaxio { CustomerFound = false };
        var service = MaxioTestHarness.Build(new StubHandler(fake.Respond));

        await service.SubscribeAsync(Shopper, "eshop-pro");

        Assert.Equal(1, fake.CreateCustomerCalls);
    }

    [Fact]
    public async Task SubscribeAsync_sends_product_handle_customer_and_collection_method()
    {
        var fake = new FakeMaxio { CustomerFound = true };
        var handler = new StubHandler(fake.Respond);
        var service = MaxioTestHarness.Build(handler, paymentCollectionMethod: "remittance");

        await service.SubscribeAsync(Shopper, "eshop-pro");

        var createBody = handler.Requests
            .Zip(handler.Bodies, (r, b) => (r, b))
            .Last(x => x.r.Method == HttpMethod.Post && x.r.RequestUri!.AbsolutePath.Contains("subscriptions")).b;

        Assert.Contains("\"product_handle\":\"eshop-pro\"", createBody);
        Assert.Contains("\"customer_id\":555", createBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", createBody);
    }

    [Fact]
    public async Task SubscribeAsync_unknown_plan_is_a_client_error()
    {
        var fake = new FakeMaxio { CustomerFound = true };
        var service = MaxioTestHarness.Build(new StubHandler(fake.Respond));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Shopper, "no-such-plan"));

        Assert.True(ex.IsClientError);
        Assert.Equal(0, fake.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribeAsync_surfaces_maxio_validation_messages_as_a_client_error()
    {
        var fake = new FakeMaxio
        {
            CustomerFound = true,
            CreateSubscriptionStatus = HttpStatusCode.UnprocessableEntity,
            CreateSubscriptionBodyOverride = FakeMaxio.SubscriptionValidationError
        };
        var service = MaxioTestHarness.Build(new StubHandler(fake.Respond));

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Shopper, "eshop-pro"));

        Assert.True(ex.IsClientError);
        Assert.Contains("No payment method was on file", ex.Message);
    }

    [Fact]
    public async Task SubscribeAsync_maps_transport_failure_to_a_non_client_error()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var service = MaxioTestHarness.Build(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(Shopper, "eshop-pro"));

        Assert.False(ex.IsClientError); // Upstream/transport fault → 502, not a 400.
    }

    [Fact]
    public async Task ListSubscriptionsAsync_returns_empty_when_no_customer_exists()
    {
        var fake = new FakeMaxio { CustomerFound = false };
        var service = MaxioTestHarness.Build(new StubHandler(fake.Respond));

        var subscriptions = await service.ListSubscriptionsAsync(Shopper);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_maps_existing_subscriptions()
    {
        var fake = new FakeMaxio { CustomerFound = true, HasActiveProSubscription = true };
        var service = MaxioTestHarness.Build(new StubHandler(fake.Respond));

        var subscriptions = (await service.ListSubscriptionsAsync(Shopper)).ToList();

        var sub = Assert.Single(subscriptions);
        Assert.Equal("eshop-pro", sub.PlanHandle);
        Assert.Equal("active", sub.State);
        Assert.NotNull(sub.NextBillingAt);
    }

    [Fact]
    public async Task SubscribeAsync_concurrent_double_click_creates_only_one_subscription()
    {
        var fake = new FakeMaxio { CustomerFound = false };
        var service = MaxioTestHarness.Build(new StubHandler(fake.Respond));

        var results = await Task.WhenAll(
            service.SubscribeAsync(Shopper, "eshop-pro"),
            service.SubscribeAsync(Shopper, "eshop-pro"));

        Assert.Equal(1, fake.CreateCustomerCalls);
        Assert.Equal(1, fake.CreateSubscriptionCalls);
        Assert.Contains(results, r => !r.AlreadyExisted);
        Assert.Contains(results, r => r.AlreadyExisted);
    }
}
