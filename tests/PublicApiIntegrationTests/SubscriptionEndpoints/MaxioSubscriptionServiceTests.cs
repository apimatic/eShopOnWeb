using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.eShopWeb.PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private sealed class CapturedRequest
    {
        public required HttpMethod Method { get; init; }
        public required string Path { get; init; }
        public string? Body { get; init; }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<CapturedRequest> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The SDK disposes the request after sending, so capture the body now.
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest { Method = request.Method, Path = request.RequestUri!.AbsolutePath, Body = body });
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static MaxioSubscriptionService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = "eshop-subscribe" });
        return new MaxioSubscriptionService(client, settings, new MemoryCache(new MemoryCacheOptions()), NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static SubscribeCommand Command() => new()
    {
        CustomerReference = "demouser@microsoft.com",
        Email = "demouser@microsoft.com",
        FirstName = "demouser",
        LastName = "demouser",
        ProductHandle = "eshop-pro",
    };

    private const string CustomerJson = """{"customer":{"id":123,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com","first_name":"demouser","last_name":"demouser"}}""";

    private const string SubscriptionJson = """{"subscription":{"id":777,"reference":"demouser@microsoft.com:eshop-pro","state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-09-25T00:00:00Z","current_period_ends_at":"2026-09-25T00:00:00Z","activated_at":"2026-08-25T00:00:00Z","product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}""";

    [TestMethod]
    public async Task ListPlans_ReturnsPlansFromConfiguredFamily()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/product_families.json"))
            {
                return Json(HttpStatusCode.OK, """[{"product_family":{"id":3023074,"handle":"eshop-subscribe"}}]""");
            }
            if (path.Contains("/product_families/3023074/products.json"))
            {
                return Json(HttpStatusCode.OK, """[{"product":{"id":7126957,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}},{"product":{"id":7126958,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}]""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync();

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task Subscribe_ExistingCustomer_CreatesSubscriptionWithDeterministicReferenceAndRemittance()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (path.EndsWith("/subscriptions/lookup.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (path.EndsWith("/subscriptions.json") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Created, SubscriptionJson);
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(Command());

        Assert.AreEqual(777, subscription.Id);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual("Pro Plan", subscription.ProductName);
        Assert.AreEqual(29900, subscription.PriceInCents);
        Assert.IsNotNull(subscription.NextBillingDate);

        var createRequest = handler.Requests.Single(r => r.Method == HttpMethod.Post);
        var sentJson = createRequest.Body ?? string.Empty;
        StringAssert.Contains(sentJson, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(sentJson, "\"customer_id\":123");
        StringAssert.Contains(sentJson, "\"reference\":\"demouser@microsoft.com:eshop-pro\"");
        StringAssert.Contains(sentJson, "\"payment_collection_method\":\"remittance\"");
    }

    [TestMethod]
    public async Task Subscribe_ExistingSubscription_IsReturnedWithoutCreating()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (path.EndsWith("/subscriptions/lookup.json"))
            {
                return Json(HttpStatusCode.OK, SubscriptionJson);
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var subscription = await service.SubscribeAsync(Command());

        Assert.AreEqual(777, subscription.Id);
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post), "A duplicate subscribe must not POST a new subscription");
    }

    [TestMethod]
    public async Task Subscribe_Provider422_SurfacesAsClientErrorWithProviderMessages()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (path.EndsWith("/subscriptions/lookup.json"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (path.EndsWith("/subscriptions.json") && request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file for the $299.00 balance"]}""");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioIntegrationException>(() => service.SubscribeAsync(Command()));

        Assert.AreEqual(422, ex.StatusCode);
        StringAssert.Contains(ex.Message, "No payment method");
    }

    [TestMethod]
    public async Task ListSubscriptions_UnknownCustomer_ReturnsEmpty()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync("nobody@microsoft.com");

        Assert.AreEqual(0, subscriptions.Count);
    }

    [TestMethod]
    public async Task ListSubscriptions_ReturnsMappedSubscriptions()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/customers/lookup.json"))
            {
                return Json(HttpStatusCode.OK, CustomerJson);
            }
            if (path.EndsWith("/customers/123/subscriptions.json"))
            {
                return Json(HttpStatusCode.OK, "[" + SubscriptionJson + "]");
            }
            return Json(HttpStatusCode.NotFound, "{}");
        });
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync("demouser@microsoft.com");

        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual("eshop-pro", subscriptions[0].ProductHandle);
        Assert.AreEqual("active", subscriptions[0].State);
    }
}
