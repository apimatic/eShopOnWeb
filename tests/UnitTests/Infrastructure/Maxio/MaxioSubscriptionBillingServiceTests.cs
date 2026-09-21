using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Tests <see cref="MaxioSubscriptionBillingService"/> against the SDK's real HTTP seam (a fake
/// <see cref="HttpMessageHandler"/>), so the actual Maxio wire (de)serialization is exercised while
/// the branching logic — fresh create vs idempotent replay, live-state filtering, plan validation —
/// is driven deterministically.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private static readonly SubscriberIdentity Subscriber =
        new("user@example.com", "user@example.com", "User", "Example");

    // --- routing stub ---

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<(HttpMethod Method, string Path)> Calls { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath));
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        public int CountPost(string pathEndsWith) =>
            Calls.Count(c => c.Method == HttpMethod.Post && c.Path.EndsWith(pathEndsWith, StringComparison.Ordinal));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (MaxioSubscriptionBillingService Service, StubHandler Handler) BuildService(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family"
        });
        var service = new MaxioSubscriptionBillingService(
            client, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
        return (service, handler);
    }

    // --- canned wire payloads (shapes match the Maxio SDK models) ---

    private const string TwoProductsJson = """
        [
          { "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
          { "product": { "id": 2, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } }
        ]
        """;

    private static string ProProductsOnlyJson => """
        [ { "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } } ]
        """;

    private static string CustomerJson(int id) => $$"""
        { "customer": { "id": {{id}}, "reference": "user@example.com", "email": "user@example.com", "first_name": "User", "last_name": "Example" } }
        """;

    private static string SubscriptionJson(int id, string state, string handle) => $$"""
        { "subscription": { "id": {{id}}, "state": "{{state}}", "product_price_in_cents": 29900,
          "current_period_ends_at": "2026-10-08T00:00:00Z", "created_at": "2026-09-08T00:00:00Z",
          "product": { "handle": "{{handle}}", "name": "Pro Plan" } } }
        """;

    private static string SubscriptionListJson(params string[] elements) => "[" + string.Join(",", elements) + "]";

    // --- tests ---

    [Fact]
    public async Task GetPlansAsync_MapsProductsFromConfiguredFamily()
    {
        var (service, handler) = BuildService(req =>
            req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/products.json")
                ? Json(HttpStatusCode.OK, TwoProductsJson)
                : Json(HttpStatusCode.InternalServerError, "{}"));

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
        // The family handle must be sent with the `handle:` prefix (bare handle 404s). The colon is
        // percent-encoded in the path, so match the encoded form.
        Assert.Contains(handler.Calls, c => c.Path.Contains("handle%3Atest-family", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SubscribeAsync_WhenNoCustomer_CreatesCustomerThenSubscription()
    {
        var (service, handler) = BuildService(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/products.json")) return Json(HttpStatusCode.OK, ProProductsOnlyJson);
            if (req.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json")) return Json(HttpStatusCode.NotFound, """{ "errors": ["not found"] }""");
            if (req.Method == HttpMethod.Post && path.EndsWith("/customers.json")) return Json(HttpStatusCode.Created, CustomerJson(555));
            if (req.Method == HttpMethod.Get && path.EndsWith("/subscriptions.json") && path.Contains("/customers/")) return Json(HttpStatusCode.OK, "[]");
            if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson(999, "active", "eshop-pro"));
            return Json(HttpStatusCode.InternalServerError, "{}");
        });

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.SubscriptionId);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(1, handler.CountPost("/customers.json"));
        Assert.Equal(1, handler.CountPost("/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_WhenLiveSubscriptionExists_IsIdempotent_NoCreate()
    {
        var (service, handler) = BuildService(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/products.json")) return Json(HttpStatusCode.OK, ProProductsOnlyJson);
            if (req.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json")) return Json(HttpStatusCode.OK, CustomerJson(555));
            if (req.Method == HttpMethod.Get && path.EndsWith("/subscriptions.json") && path.Contains("/customers/"))
                return Json(HttpStatusCode.OK, SubscriptionListJson(SubscriptionJson(999, "active", "eshop-pro")));
            // A create here would be a duplicate — fail loudly if it happens.
            return Json(HttpStatusCode.InternalServerError, "{}");
        });

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.True(result.AlreadyExisted);
        Assert.Equal(999, result.Subscription.SubscriptionId);
        Assert.Equal(0, handler.CountPost("/subscriptions.json"));
        Assert.Equal(0, handler.CountPost("/customers.json"));
    }

    [Fact]
    public async Task SubscribeAsync_IgnoresCanceledSubscription_AndCreatesNew()
    {
        var (service, handler) = BuildService(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/products.json")) return Json(HttpStatusCode.OK, ProProductsOnlyJson);
            if (req.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json")) return Json(HttpStatusCode.OK, CustomerJson(555));
            if (req.Method == HttpMethod.Get && path.EndsWith("/subscriptions.json") && path.Contains("/customers/"))
                return Json(HttpStatusCode.OK, SubscriptionListJson(SubscriptionJson(1000, "canceled", "eshop-pro")));
            if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson(1001, "active", "eshop-pro"));
            return Json(HttpStatusCode.InternalServerError, "{}");
        });

        var result = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.False(result.AlreadyExisted); // canceled subscription must not count as "already subscribed"
        Assert.Equal(1001, result.Subscription.SubscriptionId);
        Assert.Equal(1, handler.CountPost("/subscriptions.json"));
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_ThrowsPlanNotFound_AndNeverCallsWrite()
    {
        var (service, handler) = BuildService(req =>
            req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/products.json")
                ? Json(HttpStatusCode.OK, ProProductsOnlyJson)
                : Json(HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsAsync<PlanNotFoundException>(() => service.SubscribeAsync(Subscriber, "ghost-plan"));
        Assert.Equal(0, handler.CountPost("/customers.json"));
        Assert.Equal(0, handler.CountPost("/subscriptions.json"));
    }

    [Fact]
    public async Task GetMySubscriptionsAsync_WhenNoCustomer_ReturnsEmpty()
    {
        var (service, _) = BuildService(req =>
            req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/customers/lookup.json")
                ? Json(HttpStatusCode.NotFound, """{ "errors": ["not found"] }""")
                : Json(HttpStatusCode.InternalServerError, "{}"));

        var subscriptions = await service.GetMySubscriptionsAsync(Subscriber);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetMySubscriptionsAsync_MapsSubscriptions()
    {
        var (service, _) = BuildService(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json")) return Json(HttpStatusCode.OK, CustomerJson(555));
            if (req.Method == HttpMethod.Get && path.EndsWith("/subscriptions.json") && path.Contains("/customers/"))
                return Json(HttpStatusCode.OK, SubscriptionListJson(SubscriptionJson(999, "active", "eshop-pro")));
            return Json(HttpStatusCode.InternalServerError, "{}");
        });

        var subscriptions = await service.GetMySubscriptionsAsync(Subscriber);

        var only = Assert.Single(subscriptions);
        Assert.Equal(999, only.SubscriptionId);
        Assert.Equal("active", only.State);
        Assert.Equal("eshop-pro", only.PlanHandle);
        Assert.NotNull(only.NextBillingDate);
    }
}
