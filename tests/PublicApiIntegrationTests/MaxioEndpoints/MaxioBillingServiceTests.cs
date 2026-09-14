using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.MaxioEndpoints;

/// <summary>
/// Unit tests for MaxioBillingService over a stubbed HTTP seam — no network access.
/// Wire paths follow the Maxio Advanced Billing API contract documented in maxio-plan.md.
/// </summary>
[TestClass]
public class MaxioBillingServiceTests
{
    private const string FamiliesJson = """
        [{ "product_family": { "id": 101, "handle": "test-family", "name": "Test Family" } }]
        """;

    private const string ProductsJson = """
        [{ "product": {
             "id": 201, "handle": "eshop-pro", "name": "Pro Plan", "description": "The pro tier",
             "price_in_cents": 29900, "interval": 1, "interval_unit": "month",
             "product_family": { "id": 101, "handle": "test-family" } } }]
        """;

    private const string CustomerJson = """
        { "customer": { "id": 55, "reference": "user-1", "email": "demouser@test.local",
                        "first_name": "eShopOnWeb", "last_name": "Customer" } }
        """;

    private const string SubscriptionJson = """
        { "subscription": {
            "id": 901, "state": "active", "reference": "eshop-user-1-eshop-pro",
            "current_period_ends_at": "2026-10-08T00:00:00Z",
            "product_price_in_cents": 29900,
            "product": { "id": 201, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" },
            "customer": { "id": 55, "reference": "user-1" } } }
        """;

    public sealed record SentRequest(HttpMethod Method, string Path, string? Body);

    private sealed class StubMaxioHandler : HttpMessageHandler
    {
        private readonly List<SentRequest> _requests = new();
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubMaxioHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public IReadOnlyList<SentRequest> Requests => _requests;

        public int Count(HttpMethod method, string pathSuffix) =>
            _requests.Count(r => r.Method == method && r.Path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? body = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsStringAsync(ct);
            }
            _requests.Add(new SentRequest(request.Method, request.RequestUri!.AbsolutePath, body));
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static StubMaxioHandler CreateDefaultHandler(bool subscriptionLookupFindsExisting = false)
    {
        var subscribed = subscriptionLookupFindsExisting;
        return new StubMaxioHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/product_families.json", StringComparison.OrdinalIgnoreCase))
                return Json(HttpStatusCode.OK, FamiliesJson);
            if (request.Method == HttpMethod.Get && path.EndsWith("/products.json", StringComparison.OrdinalIgnoreCase))
                return Json(HttpStatusCode.OK, ProductsJson);
            if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json", StringComparison.OrdinalIgnoreCase))
                return Json(HttpStatusCode.NotFound, "{ \"error\": \"Customer not found\" }");
            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.OrdinalIgnoreCase))
                return Json(HttpStatusCode.Created, CustomerJson);
            if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json", StringComparison.OrdinalIgnoreCase))
                return subscribed
                    ? Json(HttpStatusCode.OK, SubscriptionJson)
                    : Json(HttpStatusCode.NotFound, "{ \"error\": \"Not found\" }");
            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.OrdinalIgnoreCase))
            {
                subscribed = true;
                return Json(HttpStatusCode.Created, SubscriptionJson);
            }
            return Json(HttpStatusCode.NotFound, "{ \"error\": \"Unexpected request\" }");
        });
    }

    private static MaxioBillingService CreateService(StubMaxioHandler handler)
    {
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family"
        };
        var client = MaxioClientFactory.Create(new HttpClient(handler), options);
        return new MaxioBillingService(client, options, NullLogger<MaxioBillingService>.Instance);
    }

    [TestMethod]
    public async Task ListPlansAsync_MapsProductsOfConfiguredFamily()
    {
        var handler = CreateDefaultHandler();
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual("Pro Plan", plans[0].Name);
        Assert.AreEqual(299.00m, plans[0].Price);
        Assert.AreEqual("month", plans[0].IntervalUnit);
        Assert.AreEqual(1, handler.Count(HttpMethod.Get, "/products.json"));
    }

    [TestMethod]
    public async Task SubscribeAsync_FirstCall_CreatesCustomerAndSubscriptionOnce()
    {
        var handler = CreateDefaultHandler();
        var service = CreateService(handler);

        var result = await service.SubscribeAsync("user-1", "demouser@test.local", "eshop-pro");

        Assert.IsTrue(result.Created);
        Assert.AreEqual(901, result.Subscription.Id);
        Assert.AreEqual("eshop-pro", result.Subscription.PlanHandle);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual(299.00m, result.Subscription.Price);
        Assert.IsNotNull(result.Subscription.NextBillingDate);
        Assert.AreEqual(1, handler.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, handler.Count(HttpMethod.Post, "/subscriptions.json"));

        var subscriptionBody = handler.Requests
            .Last(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/subscriptions.json"))
            .Body ?? string.Empty;
        StringAssert.Contains(subscriptionBody, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(subscriptionBody, "\"customer_reference\":\"user-1\"");
        StringAssert.Contains(subscriptionBody, "\"reference\":\"eshop-user-1-eshop-pro\"");
    }

    [TestMethod]
    public async Task SubscribeAsync_RepeatedCall_ReturnsExistingWithoutRecreating()
    {
        var handler = CreateDefaultHandler();
        var service = CreateService(handler);

        var first = await service.SubscribeAsync("user-1", "demouser@test.local", "eshop-pro");
        var second = await service.SubscribeAsync("user-1", "demouser@test.local", "eshop-pro");

        Assert.IsTrue(first.Created);
        Assert.IsFalse(second.Created);
        Assert.AreEqual(901, second.Subscription.Id);
        Assert.AreEqual(1, handler.Count(HttpMethod.Post, "/customers.json"));
        Assert.AreEqual(1, handler.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task SubscribeAsync_ExistingSubscriptionLookupHits_ReturnsWithoutCreate()
    {
        var handler = CreateDefaultHandler(subscriptionLookupFindsExisting: true);
        var service = CreateService(handler);

        var result = await service.SubscribeAsync("user-1", "demouser@test.local", "eshop-pro");

        Assert.IsFalse(result.Created);
        Assert.AreEqual(0, handler.Count(HttpMethod.Post, "/subscriptions.json"));
    }

    [TestMethod]
    public async Task SubscribeAsync_UnknownPlan_ThrowsNotFound()
    {
        var handler = CreateDefaultHandler();
        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync("user-1", "demouser@test.local", "no-such-plan"));

        Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [TestMethod]
    public async Task ListSubscriptionsForUserAsync_WhenCustomerMissing_ReturnsEmpty()
    {
        var handler = CreateDefaultHandler();
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsForUserAsync("never-seen-user");

        Assert.AreEqual(0, subscriptions.Count);
    }
}
