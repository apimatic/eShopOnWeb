using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.MaxioBilling;

[TestClass]
public class MaxioBillingServiceTests
{
    private const string ProductFamiliesJson = """
        [{"product_family":{"id":3023074,"name":"eShop Subscribe","handle":"eshop-subscribe"}}]
        """;

    private const string ProductsJson = """
        [
          {"product":{"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null}},
          {"product":{"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month","archived_at":null}}
        ]
        """;

    private const string CustomerJson = """
        {"customer":{"id":777,"reference":"user-1","email":"user@example.com","first_name":"User","last_name":"Customer"}}
        """;

    private const string SubscriptionJson = """
        {"subscription":{"id":555,"reference":"user-1:eshop-pro","state":"active","product_price_in_cents":29900,
         "current_period_ends_at":"2026-09-26T00:00:00Z","cancel_at_end_of_period":false,"canceled_at":null,
         "product":{"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900}}}
        """;

    [TestMethod]
    public async Task ListPlans_ReturnsMappedPlans()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, ProductFamiliesJson);
        handler.Enqueue(HttpStatusCode.OK, ProductsJson);
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);

        Assert.AreEqual(2, handler.Requests.Count);
        StringAssert.Contains(handler.Requests[0].RequestUri!.AbsolutePath, "/product_families.json");
        StringAssert.Contains(handler.Requests[1].RequestUri!.AbsolutePath, "/product_families/3023074/products.json");
        Assert.AreEqual("Basic", handler.Requests[0].Headers.Authorization?.Scheme);
    }

    [TestMethod]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, ProductFamiliesJson);       // resolve family
        handler.Enqueue(HttpStatusCode.OK, ProductsJson);              // list plans
        handler.Enqueue(HttpStatusCode.NotFound, "{}");                // customer lookup: miss
        handler.Enqueue(HttpStatusCode.Created, CustomerJson);         // create customer
        handler.Enqueue(HttpStatusCode.NotFound, "");                  // subscription lookup: miss
        handler.Enqueue(HttpStatusCode.Created, SubscriptionJson);     // create subscription
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(new ClaimsPrincipal(), "eshop-pro", CancellationToken.None);

        Assert.AreEqual(555, subscription.Id);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual(299.00m, subscription.Price);
        Assert.AreEqual("eshop-pro", subscription.ProductHandle);
        Assert.IsNotNull(subscription.NextBillingDate);

        var createRequest = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions.json"));
        var body = handler.BodyOf(createRequest);
        StringAssert.Contains(body, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(body, "\"customer_id\":777");
        StringAssert.Contains(body, "\"reference\":\"user-1:eshop-pro\"");
        StringAssert.Contains(body, "\"next_billing_at\":");
    }

    [TestMethod]
    public async Task Subscribe_ReturnsExistingSubscription_WhenAlreadySubscribed()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, ProductFamiliesJson);
        handler.Enqueue(HttpStatusCode.OK, ProductsJson);
        handler.Enqueue(HttpStatusCode.OK, CustomerJson);              // customer exists
        handler.Enqueue(HttpStatusCode.OK, SubscriptionJson);          // subscription exists
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(new ClaimsPrincipal(), "eshop-pro", CancellationToken.None);

        Assert.AreEqual(555, subscription.Id);
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post), "A double subscribe must not POST anything.");
    }

    [TestMethod]
    public async Task ListMySubscriptions_ReturnsEmpty_WhenCustomerNotFound()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "{}");
        var service = CreateService(handler);

        var subscriptions = await service.ListMySubscriptionsAsync(new ClaimsPrincipal(), CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ListMySubscriptions_MapsStateAndNextBillingDate()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        handler.Enqueue(HttpStatusCode.OK, $"[{SubscriptionJson}]");
        var service = CreateService(handler);

        var subscriptions = await service.ListMySubscriptionsAsync(new ClaimsPrincipal(), CancellationToken.None);

        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual("active", subscriptions[0].State);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), subscriptions[0].NextBillingDate);
    }

    [TestMethod]
    public async Task ListPlans_ProviderServerError_SurfacesAsBadGateway()
    {
        var handler = new StubHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"errors\":[\"boom\"]}", Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.ListPlansAsync(CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_ReturnsNotFound()
    {
        var handler = new StubHandler();
        handler.Enqueue(HttpStatusCode.OK, ProductFamiliesJson);
        handler.Enqueue(HttpStatusCode.OK, ProductsJson);
        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(new ClaimsPrincipal(), "no-such-plan", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
    }

    private static MaxioBillingService CreateService(StubHandler handler)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Default() with { MaxRetries = 1, Delay = TimeSpan.Zero }
        };
        clientOptions.Server.Production.Us.BaseUrl = "https://maxio.test";

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), clientOptions);
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = "eshop-subscribe"
        });

        return new MaxioBillingService(
            client,
            options,
            new MemoryCache(Options.Create(new MemoryCacheOptions())),
            new FakeUserContextAccessor(),
            NullLogger<MaxioBillingService>.Instance);
    }

    private sealed class FakeUserContextAccessor : ISubscriptionUserContextAccessor
    {
        public Task<BillingCustomerContext> GetCurrentCustomerAsync(ClaimsPrincipal principal) =>
            Task.FromResult(new BillingCustomerContext("user-1", "user@example.com", "User", "Customer"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();
        private readonly Dictionary<HttpRequestMessage, string> _bodies = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        public void Enqueue(HttpStatusCode status, string json) =>
            Enqueue(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responders.Enqueue(responder);

        public string BodyOf(HttpRequestMessage request) => _bodies[request];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            _bodies[request] = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();

            if (_responders.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"errors\":[\"unexpected request\"]}", Encoding.UTF8, "application/json")
                };
            }
            return _responders.Dequeue()(request);
        }
    }
}
