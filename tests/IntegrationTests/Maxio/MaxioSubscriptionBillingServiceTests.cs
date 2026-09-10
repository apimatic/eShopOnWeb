using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Exercises <see cref="MaxioSubscriptionBillingService"/> against a stubbed transport (the SDK's
/// HttpClient seam) — asserting real behaviour: plan mapping, the idempotency guarantee that a live
/// subscription is returned without a second create, and provider-error translation.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string FamiliesJson = "[{\"product_family\":{\"id\":123,\"handle\":\"eshop-subscribe\"}}]";
    private const string ProductsJson =
        "[{\"product\":{\"id\":1,\"name\":\"Pro Plan\",\"handle\":\"eshop-pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"product_family\":{\"handle\":\"eshop-subscribe\"}}}," +
        "{\"product\":{\"id\":2,\"name\":\"Basic Plan\",\"handle\":\"basic-plan\",\"price_in_cents\":2900,\"interval\":1,\"interval_unit\":\"month\",\"product_family\":{\"handle\":\"eshop-subscribe\"}}}]";
    private const string CustomerJson = "{\"customer\":{\"id\":555,\"reference\":\"eshop-user-demo\",\"email\":\"demo@x.com\"}}";
    private const string CreatedSubJson =
        "{\"subscription\":{\"id\":1000,\"state\":\"active\",\"product_price_in_cents\":29900,\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\",\"price_in_cents\":29900}}}";

    private static MaxioSubscriptionBillingService MakeService(StubHttpMessageHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler),
            new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = "key", Password = "x" },
                Environment = ServerEnvironment.Us
            });
        var options = Options.Create(new MaxioOptions { ApiKey = "key", Subdomain = "test", ProductFamilyHandle = FamilyHandle });
        return new MaxioSubscriptionBillingService(client, options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static HttpResponseMessage Route(HttpRequestMessage req, string customerSubsJson, HttpResponseMessage createSubResponse)
    {
        var path = req.RequestUri!.AbsolutePath;
        var get = req.Method == HttpMethod.Get;

        if (get && path.EndsWith("/products.json", StringComparison.Ordinal))
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
        if (get && path.EndsWith("/product_families.json", StringComparison.Ordinal))
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, FamiliesJson);
        if (get && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, CustomerJson);
        if (get && path.EndsWith("/subscriptions.json", StringComparison.Ordinal) && path.Contains("/customers/"))
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, customerSubsJson);
        if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            return createSubResponse;

        return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
    }

    [Fact]
    public async Task GetPlansAsync_maps_family_products_to_plans()
    {
        var handler = new StubHttpMessageHandler(req => Route(req, "[]", StubHttpMessageHandler.Json(HttpStatusCode.OK, CreatedSubJson)));
        var service = MakeService(handler);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(1, pro.IntervalCount);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_creates_subscription_when_none_live_exists()
    {
        var handler = new StubHttpMessageHandler(req => Route(req, "[]", StubHttpMessageHandler.Json(HttpStatusCode.OK, CreatedSubJson)));
        var service = MakeService(handler);

        var result = await service.SubscribeAsync(new SubscriptionEnrollmentRequest { UserIdentity = "demo@x.com", PlanHandle = "eshop-pro" });

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1000, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json")); // exactly one create
    }

    [Fact]
    public async Task SubscribeAsync_is_idempotent_when_a_live_subscription_exists()
    {
        const string liveSubs =
            "[{\"subscription\":{\"id\":42,\"state\":\"active\",\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro Plan\",\"price_in_cents\":29900}}}]";
        var handler = new StubHttpMessageHandler(req => Route(req, liveSubs, StubHttpMessageHandler.Json(HttpStatusCode.OK, CreatedSubJson)));
        var service = MakeService(handler);

        var result = await service.SubscribeAsync(new SubscriptionEnrollmentRequest { UserIdentity = "demo@x.com", PlanHandle = "eshop-pro" });

        Assert.True(result.AlreadyExisted);
        Assert.Equal(42, result.Subscription.Id);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json")); // no second create
    }

    [Fact]
    public async Task SubscribeAsync_ignores_a_canceled_subscription_for_the_same_plan()
    {
        // A canceled subscription to the plan is NOT a live hit — a new one must be created.
        const string canceledSubs =
            "[{\"subscription\":{\"id\":7,\"state\":\"canceled\",\"product\":{\"handle\":\"eshop-pro\"}}}]";
        var handler = new StubHttpMessageHandler(req => Route(req, canceledSubs, StubHttpMessageHandler.Json(HttpStatusCode.OK, CreatedSubJson)));
        var service = MakeService(handler);

        var result = await service.SubscribeAsync(new SubscriptionEnrollmentRequest { UserIdentity = "demo@x.com", PlanHandle = "eshop-pro" });

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1000, result.Subscription.Id);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_rejects_a_plan_outside_the_family()
    {
        var handler = new StubHttpMessageHandler(req => Route(req, "[]", StubHttpMessageHandler.Json(HttpStatusCode.OK, CreatedSubJson)));
        var service = MakeService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(new SubscriptionEnrollmentRequest { UserIdentity = "demo@x.com", PlanHandle = "not-a-plan" }));

        Assert.Equal(SubscriptionBillingErrorKind.InvalidRequest, ex.Kind);
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_translates_a_422_create_error_to_invalid_request()
    {
        var error = StubHttpMessageHandler.Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"No payment method was on file\"]}");
        var handler = new StubHttpMessageHandler(req => Route(req, "[]", error));
        var service = MakeService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(
            () => service.SubscribeAsync(new SubscriptionEnrollmentRequest { UserIdentity = "demo@x.com", PlanHandle = "eshop-pro" }));

        Assert.Equal(SubscriptionBillingErrorKind.InvalidRequest, ex.Kind);
    }

    [Fact]
    public async Task GetSubscriptionsForUser_returns_empty_when_no_billing_customer_exists()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
                return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}"); // customer does not exist
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
        });
        var service = MakeService(handler);

        var subs = await service.GetSubscriptionsForUserAsync("nobody@x.com");

        Assert.Empty(subs);
    }
}
