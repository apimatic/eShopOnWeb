using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace PublicApiIntegrationTests.MaxioSubscriptionServiceTests;

/// <summary>
/// Tests for the Maxio subscription boundary service, stubbing the Maxio HTTP API behind the SDK's
/// HttpClient seam so no network is involved.
/// </summary>
[TestClass]
public class MaxioSubscriptionServiceTests
{
    private const string Family = "eshop-subscribe";
    private const string Email = "buyer@example.com";

    private static readonly string SiteJson = """{ "site": { "currency": "USD" } }""";

    private static readonly string ProductsJson = """
        [
          {
            "product": {
              "id": 1,
              "handle": "eshop-pro",
              "name": "Pro Plan",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month"
            }
          },
          {
            "product": {
              "id": 2,
              "handle": "basic-plan",
              "name": "Basic Plan",
              "price_in_cents": 2900,
              "interval": 1,
              "interval_unit": "month"
            }
          }
        ]
        """;

    private static readonly string CustomerJson = $$"""
        { "customer": { "id": 123, "first_name": "Buyer", "last_name": "Example", "email": "{{Email}}", "reference": "{{Email}}" } }
        """;

    private static readonly string SubscriptionJson = """
        {
          "subscription": {
            "id": 50826,
            "state": "active",
            "reference": "eshop-pro-ref",
            "currency": "USD",
            "product_price_in_cents": 29900,
            "current_period_ends_at": "2026-10-09T00:00:00Z",
            "product": {
              "id": 1,
              "handle": "eshop-pro",
              "name": "Pro Plan",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month"
            }
          }
        }
        """;

    [TestMethod]
    public async Task ListPlansReturnsProductsWithSiteCurrency()
    {
        var handler = new StubHandler(request =>
            Path(request, "site.json") ? (HttpStatusCode.OK, SiteJson) :
            Path(request, "products.json") ? (HttpStatusCode.OK, ProductsJson) :
            (HttpStatusCode.NotFound, "{}"));

        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual("USD", pro.Currency);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscriptionWhenNoneExists()
    {
        bool lookupSawCustomer = false;
        var handler = new StubHandler(request =>
            Path(request, "site.json") ? (HttpStatusCode.OK, SiteJson) :
            Path(request, "products.json") ? (HttpStatusCode.OK, ProductsJson) :
            Path(request, "lookup.json") ? NotFoundOrCustomer(ref lookupSawCustomer) :
            Path(request, "customers.json") && IsPost(request) ? (HttpStatusCode.Created, CustomerJson) :
            Path(request, "subscriptions.json") && IsGet(request) ? (HttpStatusCode.OK, "[]") :
            Path(request, "subscriptions.json") && IsPost(request) ? (HttpStatusCode.Created, SubscriptionJson) :
            (HttpStatusCode.NotFound, "{}"));

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Email, "eshop-pro", CancellationToken.None);

        Assert.IsTrue(result.CreatedNew);
        Assert.AreEqual(50826, result.Subscription.Id);
        Assert.AreEqual("eshop-pro", result.Subscription.PlanHandle);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual(299m, result.Subscription.Price);
        Assert.AreEqual("USD", result.Subscription.Currency);

        var createCustomer = handler.Requests.Single(r => r.Method == HttpMethod.Post && Path(r, "customers.json"));
        var customerBody = createCustomer.Body ?? string.Empty;
        Assert.IsTrue(customerBody.Contains("\"reference\":\"" + Email + "\"", StringComparison.Ordinal));
        Assert.IsTrue(customerBody.Contains("\"email\":\"" + Email + "\"", StringComparison.Ordinal));

        var createSubscription = handler.Requests.Single(r => r.Method == HttpMethod.Post && Path(r, "subscriptions.json"));
        var subscriptionBody = createSubscription.Body ?? string.Empty;
        Assert.IsTrue(subscriptionBody.Contains("\"product_handle\":\"eshop-pro\"", StringComparison.Ordinal));
        Assert.IsTrue(subscriptionBody.Contains("\"customer_reference\":\"" + Email + "\"", StringComparison.Ordinal));
        Assert.IsTrue(subscriptionBody.Contains("\"payment_collection_method\":\"automatic\"", StringComparison.Ordinal));
        Assert.IsTrue(subscriptionBody.Contains("\"next_billing_at\"", StringComparison.Ordinal));
        Assert.IsTrue(subscriptionBody.Contains("\"reference\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SubscribeReturnsExistingSubscriptionWhenAlreadyLive()
    {
        var handler = new StubHandler(request =>
            Path(request, "site.json") ? (HttpStatusCode.OK, SiteJson) :
            Path(request, "products.json") ? (HttpStatusCode.OK, ProductsJson) :
            Path(request, "lookup.json") ? (HttpStatusCode.OK, CustomerJson) :
            Path(request, "subscriptions.json") && IsGet(request) ? (HttpStatusCode.OK, "[" + SubscriptionJson + "]") :
            (HttpStatusCode.NotFound, "{}"));

        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Email, "eshop-pro", CancellationToken.None);

        Assert.IsFalse(result.CreatedNew);
        Assert.AreEqual(50826, result.Subscription.Id);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post && Path(r, "subscriptions.json")));
    }

    [TestMethod]
    public async Task MySubscriptionsReturnsEmptyWhenShopperHasNoCustomer()
    {
        var handler = new StubHandler(request =>
            Path(request, "lookup.json") ? (HttpStatusCode.NotFound, "{}") :
            (HttpStatusCode.NotFound, "{}"));

        var service = CreateService(handler);

        var result = await service.ListMySubscriptionsAsync(Email, CancellationToken.None);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task MySubscriptionsListsTheCustomersSubscriptions()
    {
        var handler = new StubHandler(request =>
            Path(request, "lookup.json") ? (HttpStatusCode.OK, CustomerJson) :
            Path(request, "subscriptions.json") ? (HttpStatusCode.OK, "[" + SubscriptionJson + "]") :
            (HttpStatusCode.NotFound, "{}"));

        var service = CreateService(handler);

        var result = await service.ListMySubscriptionsAsync(Email, CancellationToken.None);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("eshop-pro", result[0].PlanHandle);
        Assert.AreEqual(299m, result[0].Price);
    }

    [TestMethod]
    public async Task SubscribeThrowsWhenPlanNotInFamily()
    {
        var handler = new StubHandler(request =>
            Path(request, "site.json") ? (HttpStatusCode.OK, SiteJson) :
            Path(request, "products.json") ? (HttpStatusCode.OK, ProductsJson) :
            (HttpStatusCode.NotFound, "{}"));

        var service = CreateService(handler);

        await Assert.ThrowsExceptionAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(Email, "missing-plan", CancellationToken.None));
    }

    [TestMethod]
    public async Task SubscribeSurfacesProviderRejectionWithDetail()
    {
        var handler = new StubHandler(request =>
            Path(request, "site.json") ? (HttpStatusCode.OK, SiteJson) :
            Path(request, "products.json") ? (HttpStatusCode.OK, ProductsJson) :
            Path(request, "lookup.json") ? (HttpStatusCode.OK, CustomerJson) :
            Path(request, "subscriptions.json") && IsGet(request) ? (HttpStatusCode.OK, "[]") :
            Path(request, "subscriptions.json") && IsPost(request) ? (HttpStatusCode.UnprocessableEntity, """{ "errors": ["No payment method was on file for the $299.00 balance"] }""") :
            (HttpStatusCode.NotFound, "{}"));

        var service = CreateService(handler);

        var exception = await Assert.ThrowsExceptionAsync<MaxioRequestRejectedException>(
            () => service.SubscribeAsync(Email, "eshop-pro", CancellationToken.None));

        StringAssert.Contains(exception.Message, "No payment method was on file");
    }

    [TestMethod]
    public async Task SubscribeSendsAtMostOneCreateEvenWhenTransportFails()
    {
        // The stub throws on the network layer (not a status), which is the SDK trigger that retries
        // POSTs. The single-send handler must refuse the resend so exactly one create reaches the wire.
        var handler = new StubHandler(request =>
            Path(request, "site.json") ? (HttpStatusCode.OK, SiteJson) :
            Path(request, "products.json") ? (HttpStatusCode.OK, ProductsJson) :
            Path(request, "lookup.json") ? (HttpStatusCode.OK, CustomerJson) :
            Path(request, "subscriptions.json") && IsGet(request) ? (HttpStatusCode.OK, "[]") :
            Path(request, "subscriptions.json") && IsPost(request) ? throw new HttpRequestException("connection reset") :
            (HttpStatusCode.NotFound, "{}"));

        var httpClient = new HttpClient(new MaxioSingleSendHandler { InnerHandler = handler });
        var client = new MaxioAdvancedBillingClient(httpClient, new MaxioAdvancedBillingClientOptions());
        var service = new MaxioSubscriptionService(
            client,
            new MaxioOptions { ProductFamilyHandle = Family },
            NullLogger<MaxioSubscriptionService>.Instance);

        await Assert.ThrowsExceptionAsync<MaxioUnavailableException>(
            () => service.SubscribeAsync(Email, "eshop-pro", CancellationToken.None));

        Assert.AreEqual(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && Path(r, "subscriptions.json")));
    }

    private static MaxioSubscriptionService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        return new MaxioSubscriptionService(
            client,
            new MaxioOptions { ProductFamilyHandle = Family },
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static bool Path(HttpRequestMessage request, string segment)
    {
        return request.RequestUri!.AbsolutePath.Contains(segment, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGet(HttpRequestMessage request) => request.Method == HttpMethod.Get;

    private static bool IsPost(HttpRequestMessage request) => request.Method == HttpMethod.Post;

    private static bool Path(CapturedRequest captured, string segment) => Path(captured.Request, segment);

    private static bool IsGet(CapturedRequest captured) => IsGet(captured.Request);

    private static bool IsPost(CapturedRequest captured) => IsPost(captured.Request);

    private static (HttpStatusCode, string) NotFoundOrCustomer(ref bool alreadySawCustomer)
    {
        if (alreadySawCustomer)
        {
            return (HttpStatusCode.OK, CustomerJson);
        }

        alreadySawCustomer = true;
        return (HttpStatusCode.NotFound, "{}");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Json)> _responder;

        public StubHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Json)> responder)
        {
            _responder = responder;
        }

        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var captured = new CapturedRequest(request, body);
            Requests.Add(captured);
            var (status, json) = _responder(request);
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return response;
        }
    }

    private sealed class CapturedRequest
    {
        public CapturedRequest(HttpRequestMessage request, string? body)
        {
            Request = request;
            Body = body;
        }

        public HttpRequestMessage Request { get; }

        public string? Body { get; }

        public HttpMethod Method => Request.Method;
    }
}
