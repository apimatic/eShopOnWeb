using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Billing.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly SubscriberIdentity Demo = MaxioBillingTestHost.Subscriber();

    [Fact]
    public async Task GetPlansAsync_maps_products_skips_archived_and_orders_by_price()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.OK, MaxioPayloads.Products());

        var plans = (await MaxioBillingTestHost.Build(handler).GetPlansAsync()).ToList();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle));
        Assert.Equal(29900, plans[1].PriceInCents);
        Assert.Equal(299.00m, plans[1].Price);
        Assert.Equal("month", plans[1].IntervalUnit);
        Assert.False(plans[1].RequiresPaymentMethod);
        Assert.False(plans[1].HasTrial);
        Assert.Equal("demo-plans", plans[1].ProductFamilyHandle);
    }

    [Fact]
    public async Task GetPlansAsync_authenticates_with_the_api_key_as_basic_user_name()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.OK, MaxioPayloads.Products());

        await MaxioBillingTestHost.Build(handler).GetPlansAsync();

        var authorization = handler.Requests.Single().Authorization;
        Assert.NotNull(authorization);
        Assert.StartsWith("Basic ", authorization);

        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(authorization!["Basic ".Length..]));
        Assert.Equal("test-api-key:x", decoded);
    }

    [Fact]
    public async Task SubscribeAsync_creates_the_customer_and_the_subscription_on_first_use()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.OK, MaxioPayloads.Products())
            .Respond(HttpMethod.Get, MaxioPayloads.SubscriptionLookupPath, HttpStatusCode.NotFound)
            .Respond(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.NotFound)
            .Respond(HttpMethod.Post, MaxioPayloads.CustomersPath, HttpStatusCode.Created, MaxioPayloads.Customer())
            .Respond(HttpMethod.Post, MaxioPayloads.SubscriptionsPath, HttpStatusCode.Created, MaxioPayloads.Subscription());

        var result = await MaxioBillingTestHost.Build(handler)
            .SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro"));

        Assert.True(result.Created);
        Assert.Equal(94211097, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal("USD", result.Subscription.Currency);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T19:11:52+05:00"), result.Subscription.NextBillingAt);

        var createCustomer = handler.LastOf(HttpMethod.Post, MaxioPayloads.CustomersPath)!;
        Assert.Contains("\"reference\":\"eshoponweb:demouser@microsoft.com\"", createCustomer.Body);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", createCustomer.Body);

        var createSubscription = handler.LastOf(HttpMethod.Post, MaxioPayloads.SubscriptionsPath)!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", createSubscription.Body);
        Assert.Contains("\"customer_reference\":\"eshoponweb:demouser@microsoft.com\"", createSubscription.Body);
        Assert.Contains("\"reference\":\"eshoponweb:demouser@microsoft.com:eshop-pro\"", createSubscription.Body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", createSubscription.Body);
    }

    [Fact]
    public async Task SubscribeAsync_reuses_an_existing_customer()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.OK, MaxioPayloads.Products())
            .Respond(HttpMethod.Get, MaxioPayloads.SubscriptionLookupPath, HttpStatusCode.NotFound)
            .Respond(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Respond(HttpMethod.Post, MaxioPayloads.SubscriptionsPath, HttpStatusCode.Created, MaxioPayloads.Subscription());

        await MaxioBillingTestHost.Build(handler).SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro"));

        Assert.Equal(0, handler.CountOf(HttpMethod.Post, MaxioPayloads.CustomersPath));
    }

    [Fact]
    public async Task SubscribeAsync_returns_the_existing_subscription_without_creating_another()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.OK, MaxioPayloads.Products())
            .Respond(HttpMethod.Get, MaxioPayloads.SubscriptionLookupPath, HttpStatusCode.OK, MaxioPayloads.Subscription());

        var result = await MaxioBillingTestHost.Build(handler)
            .SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro"));

        Assert.False(result.Created);
        Assert.Equal(94211097, result.Subscription.Id);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, MaxioPayloads.SubscriptionsPath));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, MaxioPayloads.CustomersPath));
    }

    [Fact]
    public async Task SubscribeAsync_resolves_a_lost_create_race_to_the_winners_subscription()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.OK, MaxioPayloads.Products())
            // First lookup misses, so this caller tries to create; the second lookup, after the
            // rejection, finds what the concurrent caller created.
            .Respond(HttpMethod.Get, MaxioPayloads.SubscriptionLookupPath, HttpStatusCode.NotFound)
            .Respond(HttpMethod.Get, MaxioPayloads.SubscriptionLookupPath, HttpStatusCode.OK, MaxioPayloads.Subscription())
            .Respond(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Respond(HttpMethod.Post, MaxioPayloads.SubscriptionsPath, HttpStatusCode.UnprocessableEntity, MaxioPayloads.ReferenceTakenError);

        var result = await MaxioBillingTestHost.Build(handler)
            .SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro"));

        Assert.False(result.Created);
        Assert.Equal(94211097, result.Subscription.Id);
    }

    [Fact]
    public async Task SubscribeAsync_surfaces_a_genuine_rejection_as_a_validation_failure()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.OK, MaxioPayloads.Products())
            .Respond(HttpMethod.Get, MaxioPayloads.SubscriptionLookupPath, HttpStatusCode.NotFound)
            .Respond(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Respond(HttpMethod.Post, MaxioPayloads.SubscriptionsPath, HttpStatusCode.UnprocessableEntity, MaxioPayloads.NoPaymentMethodError);

        var exception = await Assert.ThrowsAsync<BillingValidationException>(() =>
            MaxioBillingTestHost.Build(handler).SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro")));

        Assert.Contains("No payment method was on file for the $299.00 balance", exception.Errors);
    }

    [Fact]
    public async Task SubscribeAsync_refuses_a_plan_outside_the_configured_product_family()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.OK, MaxioPayloads.Products());

        var exception = await Assert.ThrowsAsync<PlanNotFoundException>(() =>
            MaxioBillingTestHost.Build(handler).SubscribeAsync(new SubscribeRequest(Demo, "some-other-product")));

        Assert.Equal("some-other-product", exception.PlanHandle);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, MaxioPayloads.SubscriptionsPath));
    }

    [Fact]
    public async Task SubscribeAsync_refuses_to_silently_reuse_a_finished_subscription()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.OK, MaxioPayloads.Products())
            .Respond(HttpMethod.Get, MaxioPayloads.SubscriptionLookupPath, HttpStatusCode.OK,
                MaxioPayloads.Subscription(state: SubscriptionStates.Canceled));

        var exception = await Assert.ThrowsAsync<SubscriptionConflictException>(() =>
            MaxioBillingTestHost.Build(handler).SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro")));

        Assert.Equal(SubscriptionStates.Canceled, exception.ExistingState);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, MaxioPayloads.SubscriptionsPath));
    }

    [Fact]
    public async Task SubscribeAsync_scopes_the_reference_with_the_idempotency_key()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.OK, MaxioPayloads.Products())
            .Respond(HttpMethod.Get, MaxioPayloads.SubscriptionLookupPath, HttpStatusCode.NotFound)
            .Respond(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Respond(HttpMethod.Post, MaxioPayloads.SubscriptionsPath, HttpStatusCode.Created, MaxioPayloads.Subscription());

        await MaxioBillingTestHost.Build(handler)
            .SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro", "renewal-2026-09"));

        var body = handler.LastOf(HttpMethod.Post, MaxioPayloads.SubscriptionsPath)!.Body;
        Assert.Contains("\"reference\":\"eshoponweb:demouser@microsoft.com:eshop-pro:renewal-2026-09\"", body);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_returns_empty_for_a_shopper_who_never_subscribed()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.NotFound);

        var subscriptions = await MaxioBillingTestHost.Build(handler).GetSubscriptionsAsync(Demo);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_returns_the_customers_subscriptions_newest_first()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.CustomerLookupPath, HttpStatusCode.OK, MaxioPayloads.Customer())
            .Respond(HttpMethod.Get, MaxioPayloads.CustomerSubscriptionsPath(98839435), HttpStatusCode.OK,
                MaxioPayloads.SubscriptionList(
                    MaxioPayloads.Subscription(id: 1, createdAt: "2026-01-01T00:00:00+05:00"),
                    MaxioPayloads.Subscription(id: 2, createdAt: "2026-05-01T00:00:00+05:00")));

        var subscriptions = (await MaxioBillingTestHost.Build(handler).GetSubscriptionsAsync(Demo)).ToList();

        Assert.Equal(new long[] { 2, 1 }, subscriptions.Select(s => s.Id));
    }

    [Fact]
    public async Task Transient_failures_are_retried()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.TooManyRequests)
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.ServiceUnavailable)
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.OK, MaxioPayloads.Products());

        var plans = await MaxioBillingTestHost.Build(handler).GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal(3, handler.CountOf(HttpMethod.Get, MaxioPayloads.ProductsPath));
    }

    [Fact]
    public async Task Exhausted_retries_surface_as_a_provider_failure()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.BadGateway);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            MaxioBillingTestHost.Build(handler).GetPlansAsync());

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal(4, handler.CountOf(HttpMethod.Get, MaxioPayloads.ProductsPath));
    }

    [Fact]
    public async Task A_rejected_api_key_is_reported_as_a_configuration_problem()
    {
        var handler = new StubMaxioHandler()
            .Respond(HttpMethod.Get, MaxioPayloads.ProductsPath, HttpStatusCode.Unauthorized, "HTTP Basic: Access denied.");

        await Assert.ThrowsAsync<BillingConfigurationException>(() =>
            MaxioBillingTestHost.Build(handler).GetPlansAsync());
    }

    [Fact]
    public async Task An_unconfigured_provider_fails_before_any_call_is_attempted()
    {
        var handler = new StubMaxioHandler();
        var service = MaxioBillingTestHost.Build(handler, new Dictionary<string, string?> { ["Maxio:ApiKey"] = null });

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => service.GetPlansAsync());

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.Empty(handler.Requests);
    }
}
