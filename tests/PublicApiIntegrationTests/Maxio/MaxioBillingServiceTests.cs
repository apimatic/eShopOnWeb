using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Maxio;

[TestClass]
public class MaxioBillingServiceTests
{
    private static readonly ShopperIdentity Shopper = new("user-1", "jane.doe@example.com", "Jane", "Doe");

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MaxioBillingService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioBillingService(client, options, NullLogger<MaxioBillingService>.Instance);
    }

    [TestMethod]
    public async Task ListPlans_ReturnsPlansInConfiguredFamily_SkippingArchived()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("product_families") && path.Contains("products"))
            {
                return Json(HttpStatusCode.OK, """
                    [
                      { "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "description": "Pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null } },
                      { "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan", "description": "Basic", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "archived_at": null } },
                      { "product": { "id": 999, "name": "Old", "handle": "old", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "archived_at": "2026-01-01T00:00:00Z" } }
                    ]
                    """);
            }
            if (path.Contains("product_families"))
            {
                return Json(HttpStatusCode.OK, """
                    [ { "product_family": { "id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe" } } ]
                    """);
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var plans = await CreateService(handler).ListPlansAsync();

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual("month", pro.IntervalUnit);
        // family id (not handle) must be used on the products path
        Assert.IsTrue(handler.Requests.Last(r => r.RequestUri!.AbsolutePath.Contains("products"))
            .RequestUri!.AbsolutePath.Contains("3023074"));
    }

    [TestMethod]
    public async Task Subscribe_ExistingCustomerNoSubscription_CreatesCustomer2AndSubscription()
    {
        string? sentBody = null;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "eshop-user-user-1", "email": "jane.doe@example.com", "first_name": "Jane", "last_name": "Doe" } }""");
            }
            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.NotFound, "");
            }
            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                // the SDK disposes request content after sending, so capture it here
                sentBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(HttpStatusCode.Created, """
                    { "subscription": { "id": 9001, "state": "active", "product": { "handle": "eshop-pro", "name": "Pro Plan" }, "product_price_in_cents": 29900, "current_period_ends_at": "2026-09-25T00:00:00Z" } }
                    """);
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var result = await CreateService(handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.AreEqual(9001, result.Id);
        Assert.AreEqual("active", result.State);
        Assert.AreEqual("eshop-pro", result.ProductHandle);
        Assert.AreEqual(29900, result.PriceInCents);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero), result.CurrentPeriodEndsAt);

        Assert.IsNotNull(sentBody);
        StringAssert.Contains(sentBody, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(sentBody, "\"customer_reference\":\"eshop-user-user-1\"");
        StringAssert.Contains(sentBody, "\"reference\":\"eshop-user-user-1-eshop-pro\"");
    }

    [TestMethod]
    public async Task Subscribe_UnknownCustomer_CreatesCustomerThenSubscription()
    {
        var customerCreated = false;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("customers"))
            {
                return customerCreated
                    ? Json(HttpStatusCode.OK, """{ "customer": { "id": 556, "reference": "eshop-user-user-1" } }""")
                    : Json(HttpStatusCode.NotFound, "{}");
            }
            if (request.Method == HttpMethod.Post && path.Contains("customers"))
            {
                customerCreated = true;
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                StringAssert.Contains(body, "\"reference\":\"eshop-user-user-1\"");
                StringAssert.Contains(body, "\"email\":\"jane.doe@example.com\"");
                StringAssert.Contains(body, "\"first_name\":\"Jane\"");
                StringAssert.Contains(body, "\"last_name\":\"Doe\"");
                return Json(HttpStatusCode.Created, """{ "customer": { "id": 556, "reference": "eshop-user-user-1" } }""");
            }
            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.NotFound, "");
            }
            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.Created, """{ "subscription": { "id": 9002, "state": "active", "product": { "handle": "basic-plan", "name": "Basic Plan" }, "product_price_in_cents": 2900 } }""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var result = await CreateService(handler).SubscribeAsync(Shopper, "basic-plan");

        Assert.AreEqual(9002, result.Id);
        Assert.IsTrue(customerCreated);
    }

    [TestMethod]
    public async Task Subscribe_ExistingSubscription_IsIdempotentAndSendsNoCreate()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "eshop-user-user-1" } }""");
            }
            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, """{ "subscription": { "id": 9001, "state": "active", "product": { "handle": "eshop-pro", "name": "Pro Plan" }, "product_price_in_cents": 29900 } }""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var result = await CreateService(handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.AreEqual(9001, result.Id);
        Assert.AreEqual(0, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task Subscribe_CreateRacesWith422_ReReadsAndReturnsExisting()
    {
        var findCalls = 0;
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "eshop-user-user-1" } }""");
            }
            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                findCalls++;
                return findCalls < 2
                    ? Json(HttpStatusCode.NotFound, "")
                    : Json(HttpStatusCode.OK, """{ "subscription": { "id": 9007, "state": "active", "product": { "handle": "eshop-pro", "name": "Pro Plan" } } }""");
            }
            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Reference must be unique"] }""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var result = await CreateService(handler).SubscribeAsync(Shopper, "eshop-pro");

        Assert.AreEqual(9007, result.Id);
    }

    [TestMethod]
    public async Task ListMySubscriptions_NoCustomer_ReturnsEmpty()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));

        var result = await CreateService(handler).ListMySubscriptionsAsync(Shopper);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task ListMySubscriptions_ReturnsMappedSubscriptions()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, """
                    [ { "subscription": { "id": 9001, "state": "active", "product": { "handle": "eshop-pro", "name": "Pro Plan" }, "product_price_in_cents": 29900, "current_period_ends_at": "2026-09-25T00:00:00Z" } } ]
                    """);
            }
            if (request.Method == HttpMethod.Get && path.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "eshop-user-user-1" } }""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });

        var result = await CreateService(handler).ListMySubscriptionsAsync(Shopper);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("active", result[0].State);
        Assert.AreEqual("Pro Plan", result[0].ProductName);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero), result[0].CurrentPeriodEndsAt);
    }
}
