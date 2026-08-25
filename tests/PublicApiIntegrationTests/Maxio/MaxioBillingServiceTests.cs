using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Maxio;

[TestClass]
public class MaxioBillingServiceTests
{
    private static readonly BillingUser User = new("user-1", "demouser@microsoft.com", "demouser", "demouser");

    [TestMethod]
    public async Task GetPlans_ReturnsMappedPlansFromConfiguredFamily()
    {
        var handler = new StubHandler(
            Json(HttpStatusCode.OK, """[{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscriptions"}}]"""),
            Json(HttpStatusCode.OK, """[{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","description":"Pro tier","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null}}]"""));
        var service = CreateService(handler);

        var plans = await service.GetPlansAsync();

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual("Pro Plan", plans[0].Name);
        Assert.AreEqual(29900, plans[0].PriceInCents);
        Assert.AreEqual(1, plans[0].Interval);
        Assert.AreEqual("month", plans[0].IntervalUnit);
    }

    [TestMethod]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var handler = new StubHandler(
            Json(HttpStatusCode.NotFound, """{"error":"Not found"}"""),
            Json(HttpStatusCode.Created, """{"customer":{"id":123,"reference":"user-1"}}"""),
            Json(HttpStatusCode.OK, """[]"""),
            Json(HttpStatusCode.Created, """{"subscription":{"id":555,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"currency":"USD","current_period_started_at":"2026-08-25T00:00:00Z","current_period_ends_at":"2026-09-25T00:00:00Z"}}"""));
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(User, "eshop-pro");

        Assert.AreEqual(555, subscription.SubscriptionId);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual("eshop-pro", subscription.ProductHandle);
        Assert.AreEqual(29900, subscription.ProductPriceInCents);
        Assert.AreEqual("USD", subscription.Currency);
        Assert.AreEqual(DateTimeOffset.Parse("2026-09-25T00:00:00Z"), subscription.NextBillingDate);
        Assert.AreEqual(2, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task Subscribe_ReturnsExistingSubscription_WhenOneIsAlreadyLive()
    {
        var handler = new StubHandler(
            Json(HttpStatusCode.OK, """{"customer":{"id":123,"reference":"user-1"}}"""),
            Json(HttpStatusCode.OK, """[{"subscription":{"id":555,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},"product_price_in_cents":29900,"currency":"USD","current_period_ends_at":"2026-09-25T00:00:00Z"}}]"""));
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(User, "eshop-pro");

        Assert.AreEqual(555, subscription.SubscriptionId);
        Assert.AreEqual(0, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task GetMySubscriptions_ReturnsEmpty_WhenUserHasNoCustomer()
    {
        var handler = new StubHandler(
            Json(HttpStatusCode.NotFound, """{"error":"Not found"}"""));
        var service = CreateService(handler);

        var subscriptions = await service.GetMySubscriptionsAsync(User);

        Assert.AreEqual(0, subscriptions.Count);
    }

    private static MaxioBillingService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = new MaxioSettings { ApiKey = "test", Subdomain = "test-site", ProductFamilyHandle = "eshop-subscribe" };
        return new MaxioBillingService(client, settings, new FakeLogger());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses;

        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<Func<HttpResponseMessage>>(responses.Select<HttpResponseMessage, Func<HttpResponseMessage>>(r =>
            {
                var used = false;
                return () =>
                {
                    // A response message can only be read once; retries get a fresh copy.
                    if (used)
                    {
                        return Json(r.StatusCode, r.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    }
                    used = true;
                    return r;
                };
            }));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
            }
            return Task.FromResult(_responses.Dequeue()());
        }
    }

    private sealed class FakeLogger : IAppLogger<MaxioBillingService>
    {
        public void LogInformation(string message, params object[] args) { }
        public void LogWarning(string message, params object[] args) { }
    }
}
