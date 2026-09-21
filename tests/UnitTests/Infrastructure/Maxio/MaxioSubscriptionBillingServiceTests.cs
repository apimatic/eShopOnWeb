using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Behavioural tests for the Maxio billing service, driven through the SDK's HttpClient seam (a fake handler)
/// so no network is touched. They assert the real behaviour the integration promises: plan mapping, the
/// ensure-customer-then-subscribe flow, idempotent dedupe (no duplicate create), and error translation.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private static readonly SubscriberIdentity Subscriber = new()
    {
        Reference = "demouser@microsoft.com",
        Email = "demouser@microsoft.com",
        FirstName = "Demo",
        LastName = "User",
    };

    private static MaxioSubscriptionBillingService BuildService(StubHttpMessageHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family",
        };
        return new MaxioSubscriptionBillingService(client, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetPlansAsync_maps_and_orders_products_by_price()
    {
        var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.OK, """
            [
              { "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
              { "product": { "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } }
            ]
            """));
        var service = BuildService(handler);

        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);   // ordered by price ascending
        Assert.Equal(2900, plans[0].PriceInCents);
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal("Pro Plan", plans[1].Name);
        Assert.Equal("month", plans[1].IntervalUnit);
        // The family selector uses the handle: prefix on the products path (the ':' is percent-encoded on the wire).
        Assert.Contains(handler.Requests, r =>
            r.Path.Contains("product_families/") && r.Path.Contains("test-family/products.json"));
    }

    [Fact]
    public async Task SubscribeAsync_creates_customer_then_subscribes_when_none_exists()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var method = request.Method.Method;

            if (method == "GET" && path.Contains("/customers/lookup.json"))
                return Json(HttpStatusCode.NotFound, "");                            // customer absent
            if (method == "POST" && path.EndsWith("/customers.json"))
                return Json(HttpStatusCode.Created, """{ "customer": { "id": 501, "reference": "demouser@microsoft.com" } }""");
            if (method == "GET" && path.Contains("/customers/501/subscriptions.json"))
                return Json(HttpStatusCode.OK, "[]");                                 // no existing subs
            if (method == "POST" && path.EndsWith("/subscriptions.json"))
                return Json(HttpStatusCode.Created, """
                    { "subscription": { "id": 9001, "state": "active", "current_period_ends_at": "2026-10-22T00:00:00Z",
                      "product_price_in_cents": 29900, "product": { "handle": "eshop-pro", "name": "Pro Plan" },
                      "customer": { "id": 501, "reference": "demouser@microsoft.com" } } }
                    """);
            return Json(HttpStatusCode.InternalServerError, "unexpected");
        });
        var service = BuildService(handler);

        var subscription = await service.SubscribeAsync(Subscriber, "eshop-pro", CancellationToken.None);

        Assert.Equal(9001, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("active", subscription.State);
        Assert.Equal(501, subscription.CustomerId);
        Assert.NotNull(subscription.NextBillingAt);
        Assert.Equal(1, handler.Count("POST", "/customers.json"));       // customer created once
        Assert.Equal(1, handler.Count("POST", "/subscriptions.json"));   // subscription created once
    }

    [Fact]
    public async Task SubscribeAsync_is_idempotent_reuses_existing_active_subscription()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var method = request.Method.Method;

            if (method == "GET" && path.Contains("/customers/lookup.json"))
                return Json(HttpStatusCode.OK, """{ "customer": { "id": 501, "reference": "demouser@microsoft.com" } }""");
            if (method == "GET" && path.Contains("/customers/501/subscriptions.json"))
                return Json(HttpStatusCode.OK, """
                    [ { "subscription": { "id": 8000, "state": "active",
                        "product": { "handle": "eshop-pro", "name": "Pro Plan" },
                        "customer": { "id": 501 } } } ]
                    """);
            if (method == "POST" && path.EndsWith("/subscriptions.json"))
                return Json(HttpStatusCode.Created, """{ "subscription": { "id": 9999, "state": "active" } }""");
            return Json(HttpStatusCode.InternalServerError, "unexpected");
        });
        var service = BuildService(handler);

        var subscription = await service.SubscribeAsync(Subscriber, "eshop-pro", CancellationToken.None);

        Assert.Equal(8000, subscription.Id);                              // existing one reused
        Assert.Equal(0, handler.Count("POST", "/subscriptions.json"));    // NO duplicate create
        Assert.Equal(0, handler.Count("POST", "/customers.json"));        // existing customer reused
    }

    [Fact]
    public async Task SubscribeAsync_translates_422_to_validation_with_provider_messages()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var method = request.Method.Method;

            if (method == "GET" && path.Contains("/customers/lookup.json"))
                return Json(HttpStatusCode.OK, """{ "customer": { "id": 501 } }""");
            if (method == "GET" && path.Contains("/customers/501/subscriptions.json"))
                return Json(HttpStatusCode.OK, "[]");
            if (method == "POST" && path.EndsWith("/subscriptions.json"))
                return Json((HttpStatusCode)422, """{ "errors": ["Product with API Handle 'nope' does not exist for this site."] }""");
            return Json(HttpStatusCode.InternalServerError, "unexpected");
        });
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync(Subscriber, "nope", CancellationToken.None));

        Assert.Equal(MaxioBillingErrorKind.Validation, ex.Kind);
        Assert.Contains(ex.ProviderMessages, m => m.Contains("does not exist for this site"));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_returns_empty_when_customer_absent()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/customers/lookup.json")
                ? Json(HttpStatusCode.NotFound, "")
                : Json(HttpStatusCode.InternalServerError, "should not be called"));
        var service = BuildService(handler);

        var subscriptions = await service.GetSubscriptionsAsync(Subscriber, CancellationToken.None);

        Assert.Empty(subscriptions);
        Assert.Equal(0, handler.Count("GET", "/subscriptions.json"));   // never listed subs for a missing customer
    }
}
