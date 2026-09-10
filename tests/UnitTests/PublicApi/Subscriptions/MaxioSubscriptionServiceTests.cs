using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscriptions;

/// <summary>
/// Tests for the Maxio integration layer. The SDK's HttpClient constructor argument is the seam:
/// a routing stub answers each Maxio route so no real network call happens.
/// </summary>
public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private const string UserName = "demouser@microsoft.com";

    // A stub that routes by method+path, records requests, and buffers bodies while readable.
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _route;
        public List<(HttpMethod Method, string Path, string Query, string? Body)> Calls { get; } = new();

        public RoutingHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> route) => _route = route;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content?.ReadAsStringAsync().Result;
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, request.RequestUri!.Query, body));
            var response = _route(request, body);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (MaxioSubscriptionService Service, RoutingHandler Handler) BuildService(
        Func<HttpRequestMessage, string?, HttpResponseMessage> route)
    {
        var handler = new RoutingHandler(route);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "key", Password = "x" }
        });
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "cp-test",
            ProductFamilyHandle = FamilyHandle,
            PaymentCollectionMethod = "invoice"
        });
        var service = new MaxioSubscriptionService(client, settings, NullLogger<MaxioSubscriptionService>.Instance);
        return (service, handler);
    }

    private static bool IsProducts(HttpRequestMessage r) =>
        r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/product_families/");
    private static bool IsCustomerLookup(HttpRequestMessage r) =>
        r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/customers/lookup.json");
    private static bool IsCreateCustomer(HttpRequestMessage r) =>
        r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/customers.json");
    private static bool IsCustomerSubs(HttpRequestMessage r) =>
        r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/customers/") &&
        r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json");
    private static bool IsCreateSubscription(HttpRequestMessage r) =>
        r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json");

    private const string ProductsJson =
        """[{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""";
    private const string CustomerJson =
        """{"customer":{"id":555,"reference":"eshoponweb:demouser@microsoft.com","email":"demouser@microsoft.com"}}""";
    private const string CreatedSubJson =
        """{"subscription":{"id":9001,"state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-10-10T00:00:00+00:00","product":{"id":1,"name":"Pro Plan","handle":"eshop-pro"}}}""";

    [Fact]
    public async Task ListPlans_maps_products_and_uses_handle_prefixed_family_path()
    {
        var (service, handler) = BuildService((r, _) =>
            IsProducts(r) ? Json(HttpStatusCode.OK, ProductsJson) : Json(HttpStatusCode.NotFound, "{}"));

        var plans = await service.ListPlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(299m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
        // Family handle must be sent prefixed with handle: in the {product_family_id} path segment
        // (the colon may be percent-encoded on the wire).
        Assert.Contains(handler.Calls, c =>
            Uri.UnescapeDataString(c.Path).Contains($"/product_families/handle:{FamilyHandle}/products.json"));
    }

    [Fact]
    public async Task Subscribe_creates_when_no_live_subscription_and_reuses_existing_customer()
    {
        var (service, handler) = BuildService((r, _) =>
        {
            if (IsProducts(r)) return Json(HttpStatusCode.OK, ProductsJson);
            if (IsCustomerLookup(r)) return Json(HttpStatusCode.OK, CustomerJson);        // customer exists
            if (IsCustomerSubs(r)) return Json(HttpStatusCode.OK, "[]");                  // no subscriptions yet
            if (IsCreateSubscription(r)) return Json(HttpStatusCode.Created, CreatedSubJson);
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var outcome = await service.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None);

        Assert.False(outcome.AlreadySubscribed);
        Assert.Equal(9001, outcome.Subscription.Id);
        Assert.Equal("active", outcome.Subscription.State);
        Assert.Equal(299m, outcome.Subscription.Price);
        Assert.NotNull(outcome.Subscription.NextBillingDate);

        // Existing customer reused ⇒ no create-customer POST.
        Assert.DoesNotContain(handler.Calls, c => c.Method == HttpMethod.Post && c.Path.EndsWith("/customers.json"));
        // The create-subscription body carries the product handle + the configured collection method.
        var createBody = handler.Calls.Single(c => c.Path.EndsWith("/subscriptions.json") && c.Method == HttpMethod.Post).Body;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", createBody);
        Assert.Contains("\"customer_id\":555", createBody);
        Assert.Contains("\"payment_collection_method\":\"invoice\"", createBody);
    }

    [Fact]
    public async Task Subscribe_creates_customer_when_missing()
    {
        var created = false;
        var (service, handler) = BuildService((r, _) =>
        {
            if (IsProducts(r)) return Json(HttpStatusCode.OK, ProductsJson);
            if (IsCustomerLookup(r))
                return created ? Json(HttpStatusCode.OK, CustomerJson) : Json(HttpStatusCode.NotFound, "{}");
            if (IsCreateCustomer(r)) { created = true; return Json(HttpStatusCode.Created, CustomerJson); }
            if (IsCustomerSubs(r)) return Json(HttpStatusCode.OK, "[]");
            if (IsCreateSubscription(r)) return Json(HttpStatusCode.Created, CreatedSubJson);
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var outcome = await service.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None);

        Assert.False(outcome.AlreadySubscribed);
        var createCustomer = handler.Calls.Single(c => IsCreateCustomerCall(c));
        Assert.Contains("\"reference\":\"eshoponweb:demouser@microsoft.com\"", createCustomer.Body);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", createCustomer.Body);
    }

    [Fact]
    public async Task Subscribe_is_idempotent_when_a_live_subscription_exists()
    {
        const string liveSubs =
            """[{"subscription":{"id":9001,"state":"active","product_price_in_cents":29900,"product":{"id":1,"handle":"eshop-pro"}}}]""";
        var (service, handler) = BuildService((r, _) =>
        {
            if (IsProducts(r)) return Json(HttpStatusCode.OK, ProductsJson);
            if (IsCustomerLookup(r)) return Json(HttpStatusCode.OK, CustomerJson);
            if (IsCustomerSubs(r)) return Json(HttpStatusCode.OK, liveSubs);
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var outcome = await service.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None);

        Assert.True(outcome.AlreadySubscribed);
        Assert.Equal(9001, outcome.Subscription.Id);
        // No new subscription created.
        Assert.DoesNotContain(handler.Calls, c => IsCreateSubscriptionCall(c));
    }

    [Fact]
    public async Task Subscribe_with_missing_plan_handle_throws_400()
    {
        var (service, _) = BuildService((r, _) =>
            IsProducts(r) ? Json(HttpStatusCode.OK, ProductsJson) : Json(HttpStatusCode.NotFound, "{}"));

        var ex = await Assert.ThrowsAsync<MaxioApiException>(
            () => service.SubscribeAsync(UserName, null, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Subscribe_with_unknown_plan_handle_throws_400()
    {
        var (service, _) = BuildService((r, _) =>
            IsProducts(r) ? Json(HttpStatusCode.OK, ProductsJson) : Json(HttpStatusCode.NotFound, "{}"));

        var ex = await Assert.ThrowsAsync<MaxioApiException>(
            () => service.SubscribeAsync(UserName, "nope", CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Subscribe_surfaces_provider_422_validation_error()
    {
        var (service, _) = BuildService((r, _) =>
        {
            if (IsProducts(r)) return Json(HttpStatusCode.OK, ProductsJson);
            if (IsCustomerLookup(r)) return Json(HttpStatusCode.OK, CustomerJson);
            if (IsCustomerSubs(r)) return Json(HttpStatusCode.OK, "[]");
            if (IsCreateSubscription(r))
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file for the $299.00 balance"]}""");
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var ex = await Assert.ThrowsAsync<MaxioApiException>(
            () => service.SubscribeAsync(UserName, "eshop-pro", CancellationToken.None));
        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("No payment method", ex.Message);
    }

    [Fact]
    public async Task ListMySubscriptions_returns_empty_when_customer_absent()
    {
        var (service, handler) = BuildService((r, _) =>
            IsCustomerLookup(r) ? Json(HttpStatusCode.NotFound, "{}") : Json(HttpStatusCode.NotFound, "{}"));

        var subs = await service.ListMySubscriptionsAsync(UserName, CancellationToken.None);

        Assert.Empty(subs);
        // A missing customer must not attempt to list subscriptions.
        Assert.DoesNotContain(handler.Calls, IsCustomerSubsCall);
    }

    private static bool IsCreateCustomerCall((HttpMethod Method, string Path, string Query, string? Body) c) =>
        c.Method == HttpMethod.Post && c.Path.EndsWith("/customers.json");
    private static bool IsCreateSubscriptionCall((HttpMethod Method, string Path, string Query, string? Body) c) =>
        c.Method == HttpMethod.Post && c.Path.EndsWith("/subscriptions.json");
    private static bool IsCustomerSubsCall((HttpMethod Method, string Path, string Query, string? Body) c) =>
        c.Method == HttpMethod.Get && c.Path.Contains("/customers/") && c.Path.EndsWith("/subscriptions.json");
}
