using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Drives <see cref="MaxioBillingService"/> against a stubbed HTTP handler to verify the
/// integration contract and, above all, the idempotency behaviour (find-or-create customer,
/// no duplicate subscriptions, race-safe on Maxio's unique-reference rejection).
/// </summary>
public class MaxioBillingServiceTests
{
    private const string ProductsJson = """
        [
          { "product": { "id": 1, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } },
          { "product": { "id": 2, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
          { "product": { "id": 3, "name": "Retired", "handle": "old-plan", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "archived_at": "2020-01-01T00:00:00-05:00" } }
        ]
        """;

    private static string SubscriptionJson(long id, string state, string handle, int priceCents, string reference) => $$"""
        { "subscription": {
            "id": {{id}}, "state": "{{state}}", "reference": "{{reference}}", "currency": "USD",
            "current_period_started_at": "2026-09-10T00:00:00-05:00",
            "current_period_ends_at": "2026-10-10T00:00:00-05:00",
            "next_assessment_at": "2026-10-10T00:00:00-05:00",
            "product": { "id": 2, "name": "Pro Plan", "handle": "{{handle}}", "price_in_cents": {{priceCents}}, "interval": 1, "interval_unit": "month" },
            "customer": { "id": 55, "reference": "demo@example.com", "email": "demo@example.com" }
        } }
        """;

    private static readonly SubscriberIdentity Subscriber = new("demo@example.com", "demo@example.com");

    private static MaxioSettings Settings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private static MaxioBillingService CreateService(StubHttpMessageHandler handler)
    {
        var settings = Settings();
        var client = new HttpClient(handler) { BaseAddress = settings.ResolveBaseUri() };
        return new MaxioBillingService(client, settings, NullLogger<MaxioBillingService>.Instance);
    }

    [Fact]
    public async Task ListPlansAsync_MapsProductsAndExcludesArchived()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.DoesNotContain(plans, p => p.Handle == "old-plan");
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscription_WithoutCreating_WhenReferenceExists()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Path.EndsWith("/products.json")) return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Path == "/subscriptions/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.OK, SubscriptionJson(999, "active", "eshop-pro", 29900, "demo@example.com:eshop-pro"));
            throw new InvalidOperationException($"Unexpected call: {req.Method} {req.Path}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(999, result.Id);
        Assert.Equal("active", result.State);
        // Idempotent fast path: no customer creation and no subscription POST.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Path.EndsWith("/products.json")) return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Path == "/subscriptions/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
            if (req.Path == "/customers/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
            if (req.Path == "/customers.json" && req.Method == HttpMethod.Post)
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, """{ "customer": { "id": 55, "reference": "demo@example.com", "email": "demo@example.com" } }""");
            if (req.Path == "/subscriptions.json" && req.Method == HttpMethod.Post)
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson(1000, "active", "eshop-pro", 29900, "demo@example.com:eshop-pro"));
            throw new InvalidOperationException($"Unexpected call: {req.Method} {req.Path}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(1000, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 5, 0, 0, TimeSpan.Zero), result.NextBillingAt!.Value.ToUniversalTime());
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.Path == "/customers.json");
        var createSub = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json");
        Assert.Contains("\"reference\":\"demo@example.com:eshop-pro\"", createSub.Body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", createSub.Body);
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotRecreateCustomer_WhenCustomerAlreadyExists()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Path.EndsWith("/products.json")) return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Path == "/subscriptions/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
            if (req.Path == "/customers/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "customer": { "id": 55, "reference": "demo@example.com", "email": "demo@example.com" } }""");
            if (req.Path == "/subscriptions.json" && req.Method == HttpMethod.Post)
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson(1001, "active", "eshop-pro", 29900, "demo@example.com:eshop-pro"));
            throw new InvalidOperationException($"Unexpected call: {req.Method} {req.Path}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(1001, result.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post && r.Path == "/customers.json");
    }

    [Fact]
    public async Task SubscribeAsync_CreatesFreshSubscription_WhenPriorEnrolmentIsCanceled()
    {
        string? postedBody = null;
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Path.EndsWith("/products.json")) return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            // The canonical reference is held by a CANCELED subscription.
            if (req.Path == "/subscriptions/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.OK, SubscriptionJson(500, "canceled", "eshop-pro", 29900, "demo@example.com:eshop-pro"));
            if (req.Path == "/customers/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "customer": { "id": 55, "reference": "demo@example.com" } }""");
            if (req.Path == "/subscriptions.json" && req.Method == HttpMethod.Post)
            {
                postedBody = req.Body;
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson(600, "active", "eshop-pro", 29900, "demo@example.com:eshop-pro:20260101000000"));
            }
            throw new InvalidOperationException($"Unexpected call: {req.Method} {req.Path}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        // A new, active subscription was created rather than returning the canceled one.
        Assert.Equal(600, result.Id);
        Assert.Equal("active", result.State);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && r.Path == "/subscriptions.json");
        // The new subscription uses a fresh (non-canonical) reference so it does not collide.
        Assert.Contains("demo@example.com:eshop-pro:", postedBody);
        Assert.DoesNotContain("\"reference\":\"demo@example.com:eshop-pro\"", postedBody);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsWinner_OnDuplicateReferenceRace()
    {
        var lookupCalls = 0;
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Path.EndsWith("/products.json")) return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Path == "/subscriptions/lookup.json")
            {
                // First lookup: not found (nobody has created it yet). After the 422, it exists.
                lookupCalls++;
                return lookupCalls == 1
                    ? StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}")
                    : StubHttpMessageHandler.Json(HttpStatusCode.OK, SubscriptionJson(1002, "active", "eshop-pro", 29900, "demo@example.com:eshop-pro"));
            }
            if (req.Path == "/customers/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "customer": { "id": 55, "reference": "demo@example.com" } }""");
            if (req.Path == "/subscriptions.json" && req.Method == HttpMethod.Post)
                return StubHttpMessageHandler.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Reference: must be unique - that value has been taken."] }""");
            throw new InvalidOperationException($"Unexpected call: {req.Method} {req.Path}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(1002, result.Id);
        Assert.Equal("active", result.State);
    }

    [Fact]
    public async Task SubscribeAsync_Throws_WhenPlanHandleUnknown()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Path.EndsWith("/products.json")) return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            throw new InvalidOperationException($"Unexpected call: {req.Method} {req.Path}");
        });
        var service = CreateService(handler);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(Subscriber, "nope"));
    }

    [Fact]
    public async Task SubscribeAsync_DefaultsToHighestPricedPlan_WhenHandleOmitted()
    {
        string? postedBody = null;
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Path.EndsWith("/products.json")) return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Path == "/subscriptions/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
            if (req.Path == "/customers/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "customer": { "id": 55, "reference": "demo@example.com" } }""");
            if (req.Path == "/subscriptions.json" && req.Method == HttpMethod.Post)
            {
                postedBody = req.Body;
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, SubscriptionJson(1003, "active", "eshop-pro", 29900, "demo@example.com:eshop-pro"));
            }
            throw new InvalidOperationException($"Unexpected call: {req.Method} {req.Path}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Subscriber, planHandle: null);

        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", postedBody);
    }

    [Fact]
    public async Task ListSubscriptionsForUserAsync_ReturnsEmpty_WhenNoCustomer()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Path == "/customers/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
            throw new InvalidOperationException($"Unexpected call: {req.Method} {req.Path}");
        });
        var service = CreateService(handler);

        var result = await service.ListSubscriptionsForUserAsync(Subscriber);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListSubscriptionsForUserAsync_MapsSubscriptions_WhenCustomerExists()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Path == "/customers/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "customer": { "id": 55, "reference": "demo@example.com" } }""");
            if (req.Path == "/customers/55/subscriptions.json") return StubHttpMessageHandler.Json(HttpStatusCode.OK, $"[ {SubscriptionJson(1004, "active", "eshop-pro", 29900, "demo@example.com:eshop-pro")} ]");
            throw new InvalidOperationException($"Unexpected call: {req.Method} {req.Path}");
        });
        var service = CreateService(handler);

        var result = await service.ListSubscriptionsForUserAsync(Subscriber);

        var only = Assert.Single(result);
        Assert.Equal(1004, only.Id);
        Assert.Equal("eshop-pro", only.PlanHandle);
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsBillingGatewayException_OnServerError()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            if (req.Path.EndsWith("/products.json")) return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Path == "/subscriptions/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
            if (req.Path == "/customers/lookup.json") return StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "boom");
            throw new InvalidOperationException($"Unexpected call: {req.Method} {req.Path}");
        });
        var service = CreateService(handler);

        await Assert.ThrowsAsync<BillingGatewayException>(() => service.SubscribeAsync(Subscriber, "eshop-pro"));
    }
}
