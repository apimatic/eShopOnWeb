using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    private const string ProductsJson = """
        [
          { "product": { "id": 1, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "product_family": { "handle": "eshop-subscribe" } } },
          { "product": { "id": 2, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "product_family": { "handle": "eshop-subscribe" } } },
          { "product": { "id": 3, "name": "Retired", "handle": "old-plan", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "archived_at": "2020-01-01T00:00:00Z" } }
        ]
        """;

    private static StubHttpMessageHandler Handler(Func<HttpRequestMessage, HttpResponseMessage> responder) => new(responder);

    private static MaxioSubscriptionBillingService BuildService(StubHttpMessageHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "key", Password = "x" }
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "sub",
            ProductFamilyHandle = FamilyHandle
        });
        return new MaxioSubscriptionBillingService(client, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private static string Path(HttpRequestMessage r) => r.RequestUri!.AbsolutePath;

    [Fact]
    public async Task GetPlansAsync_maps_live_plans_and_excludes_archived()
    {
        var handler = Handler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));
        var service = BuildService(handler);

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count); // archived "old-plan" excluded
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal("eshop-subscribe", pro.ProductFamilyHandle);
    }

    [Fact]
    public async Task GetPlansAsync_addresses_family_by_handle_prefix()
    {
        var handler = Handler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));
        var service = BuildService(handler);

        await service.GetPlansAsync();

        var request = handler.Requests.Single();
        // product_family_id path segment must be the handle prefixed with "handle:" (URL-encoded).
        Assert.Contains("/product_families/handle%3Aeshop-subscribe/products.json", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task SubscribeAsync_creates_customer_and_subscription_when_none_exist()
    {
        var handler = Handler(request =>
        {
            var path = Path(request);
            if (request.Method == HttpMethod.Get && path.Contains("/products.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, ""); // no customer yet
            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "user@x.com" } }""");
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json") && path.Contains("/customers/"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]"); // no existing subscriptions
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                    """{ "subscription": { "id": 9001, "state": "active", "product_price_in_cents": 29900, "currency": "USD", "reference": "eshop-user@x.com-eshop-pro", "product": { "handle": "eshop-pro", "name": "Pro Plan" } } }""");
            return StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "unexpected");
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(
            new SubscribeRequest("user@x.com", "user@x.com", "user", "Subscriber", "eshop-pro"));

        Assert.Equal(9001, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);

        // A customer and a subscription were actually created.
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Post && Path(r).EndsWith("/customers.json"));
        var createSub = handler.Requests.FindIndex(r => r.Method == HttpMethod.Post && Path(r).EndsWith("/subscriptions.json"));
        Assert.True(createSub >= 0);
        var body = handler.Bodies[createSub];
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":555", body);
        Assert.Contains("eshop-user@x.com-eshop-pro", body); // deterministic reference
    }

    [Fact]
    public async Task SubscribeAsync_is_idempotent_when_live_subscription_exists()
    {
        var handler = Handler(request =>
        {
            var path = Path(request);
            if (request.Method == HttpMethod.Get && path.Contains("/products.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "customer": { "id": 555 } }""");
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json") && path.Contains("/customers/"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                    """[ { "subscription": { "id": 7777, "state": "active", "product": { "handle": "eshop-pro" } } } ]""");
            return StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "should not be called");
        });
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(
            new SubscribeRequest("user@x.com", "user@x.com", "user", "Subscriber", "eshop-pro"));

        Assert.Equal(7777, result.Id); // existing subscription returned
        // No new customer or subscription was created.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_unknown_plan_is_a_validation_error()
    {
        var handler = Handler(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(
            new SubscribeRequest("user@x.com", "user@x.com", "user", "Subscriber", "no-such-plan")));

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task SubscribeAsync_provider_422_maps_to_validation_with_messages()
    {
        var handler = Handler(request =>
        {
            var path = Path(request);
            if (request.Method == HttpMethod.Get && path.Contains("/products.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (request.Method == HttpMethod.Get && path.Contains("/customers/lookup.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{ "customer": { "id": 555 } }""");
            if (request.Method == HttpMethod.Get && path.Contains("/subscriptions.json") && path.Contains("/customers/"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, "[]");
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return StubHttpMessageHandler.Json((HttpStatusCode)422, """{ "errors": ["Product handle is invalid"] }""");
            return StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "unexpected");
        });
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(
            new SubscribeRequest("user@x.com", "user@x.com", "user", "Subscriber", "eshop-pro")));

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
        Assert.Contains("Product handle is invalid", ex.Errors);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_returns_empty_when_customer_missing()
    {
        var handler = Handler(request =>
        {
            if (request.Method == HttpMethod.Get && Path(request).Contains("/customers/lookup.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "");
            return StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "should not be called");
        });
        var service = BuildService(handler);

        var result = await service.GetSubscriptionsAsync("user@x.com");

        Assert.Empty(result);
        // A read never creates a customer.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }
}
