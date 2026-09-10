using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBillingServiceTests
{
    // Fictional fixtures — deliberately NOT the real catalog handles/site (no real config values in tests).
    private const string FamilyHandle = "test-family";

    // A family whose handle matches settings, returned by GET /product_families.json
    private const string FamiliesJson = """
        [ { "product_family": { "id": 111, "handle": "test-family", "name": "Test Family" } } ]
        """;

    // Two plans + one plan with no handle (must be skipped), returned by .../products.json
    private const string ProductsJson = """
        [
          { "product": { "id": 1, "name": "Basic Plan", "handle": "basic", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } },
          { "product": { "id": 2, "name": "Pro Plan",   "handle": "pro",   "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
          { "product": { "id": 3, "name": "No Handle",   "price_in_cents": 100 } }
        ]
        """;

    private static MaxioBillingService CreateService(RoutingStubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-sub",
            ProductFamilyHandle = FamilyHandle,
            TimeoutSeconds = 30,
        });
        return new MaxioBillingService(client, settings, new KeyedSemaphore(), NullLogger<MaxioBillingService>.Instance);
    }

    [Fact]
    public async Task ListPlansAsync_MapsProductsAndSkipsHandleless()
    {
        var handler = new RoutingStubHandler((method, path, _) =>
        {
            if (method == "GET" && path.EndsWith("/product_families.json")) return RoutingStubHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (method == "GET" && path.EndsWith("/products.json")) return RoutingStubHandler.Json(HttpStatusCode.OK, ProductsJson);
            return RoutingStubHandler.Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count); // the handleless product is skipped
        var pro = Assert.Single(plans, p => p.Handle == "pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_WhenSubscriptionAlreadyExists_ReturnsItWithoutCreating()
    {
        const string existingSub = """
            { "subscription": { "id": 999, "state": "active", "product_price_in_cents": 29900,
              "reference": "eshop:demo@x.com:pro", "product": { "handle": "pro", "name": "Pro Plan" } } }
            """;
        var handler = new RoutingStubHandler((method, path, _) =>
        {
            if (method == "GET" && path.EndsWith("/product_families.json")) return RoutingStubHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (method == "GET" && path.EndsWith("/products.json")) return RoutingStubHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (method == "GET" && path.EndsWith("/subscriptions/lookup.json")) return RoutingStubHandler.Json(HttpStatusCode.OK, existingSub);
            return RoutingStubHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);
        var user = new BillingUser("demo@x.com", "demo@x.com", "demo", "eShopOnWeb");

        var result = await service.SubscribeAsync(user, "pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.SubscriptionId);
        Assert.Equal("active", result.Subscription.State);
        // Idempotency: no customer or subscription was created.
        Assert.Equal(0, handler.CountOf("POST", "/subscriptions.json"));
        Assert.Equal(0, handler.CountOf("POST", "/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenNew_EnsuresCustomerAndCreatesRemittanceSubscription()
    {
        string? createSubBody = null;
        var handler = new RoutingStubHandler((method, path, body) =>
        {
            if (method == "GET" && path.EndsWith("/product_families.json")) return RoutingStubHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (method == "GET" && path.EndsWith("/products.json")) return RoutingStubHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (method == "GET" && path.EndsWith("/subscriptions/lookup.json")) return RoutingStubHandler.Json(HttpStatusCode.NotFound, "{}");
            if (method == "GET" && path.EndsWith("/customers/lookup.json")) return RoutingStubHandler.Json(HttpStatusCode.NotFound, "{}");
            if (method == "POST" && path.EndsWith("/customers.json")) return RoutingStubHandler.Json(HttpStatusCode.Created, """{ "customer": { "id": 777 } }""");
            if (method == "POST" && path.EndsWith("/subscriptions.json"))
            {
                createSubBody = body;
                return RoutingStubHandler.Json(HttpStatusCode.Created, """
                    { "subscription": { "id": 1001, "state": "active", "product_price_in_cents": 29900,
                      "current_period_ends_at": "2026-10-10T00:00:00Z", "reference": "eshop:demo@x.com:pro",
                      "product": { "handle": "pro", "name": "Pro Plan" } } }
                    """);
            }
            return RoutingStubHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);
        var user = new BillingUser("demo@x.com", "demo@x.com", "demo", "eShopOnWeb");

        var result = await service.SubscribeAsync(user, "pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(1001, result.Subscription.SubscriptionId);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(1, handler.CountOf("POST", "/customers.json"));
        Assert.Equal(1, handler.CountOf("POST", "/subscriptions.json"));
        Assert.NotNull(createSubBody);
        Assert.Contains("\"product_handle\":\"pro\"", createSubBody);
        Assert.Contains("\"customer_id\":777", createSubBody);
        Assert.Contains("\"reference\":\"eshop:demo@x.com:pro\"", createSubBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", createSubBody);
    }

    [Fact]
    public async Task SubscribeAsync_WhenPlanHandleUnknown_ThrowsNotFoundBillingException()
    {
        var handler = new RoutingStubHandler((method, path, _) =>
        {
            if (method == "GET" && path.EndsWith("/product_families.json")) return RoutingStubHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (method == "GET" && path.EndsWith("/products.json")) return RoutingStubHandler.Json(HttpStatusCode.OK, ProductsJson);
            return RoutingStubHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);
        var user = new BillingUser("demo@x.com", "demo@x.com", "demo", "eShopOnWeb");

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(user, "no-such-plan"));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(0, handler.CountOf("POST", "/subscriptions.json")); // nothing created on a bad plan
    }

    [Fact]
    public async Task ListSubscriptionsAsync_WhenNoCustomer_ReturnsEmptyAndDoesNotListSubscriptions()
    {
        var handler = new RoutingStubHandler((method, path, _) =>
        {
            if (method == "GET" && path.EndsWith("/customers/lookup.json")) return RoutingStubHandler.Json(HttpStatusCode.NotFound, "{}");
            return RoutingStubHandler.Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var subs = await service.ListSubscriptionsAsync("unknown@x.com");

        Assert.Empty(subs);
        Assert.Equal(0, handler.CountOf("GET", "/subscriptions.json"));
    }

    [Fact]
    public async Task ListPlansAsync_WhenProviderRejectsCredentials_ThrowsBillingException502()
    {
        var handler = new RoutingStubHandler((method, path, _) =>
            RoutingStubHandler.Json(HttpStatusCode.Unauthorized, """{ "error": "unauthorized" }"""));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync());
        Assert.Equal(502, ex.StatusCode); // our-credentials failure is never surfaced as the caller's fault
    }
}
