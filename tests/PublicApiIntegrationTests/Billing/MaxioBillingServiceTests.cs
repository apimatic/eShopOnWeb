using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Billing;

[TestClass]
public class MaxioBillingServiceTests
{
    private const string Username = "demouser@microsoft.com";
    private const string FamilyHandle = "eshop-subscribe";

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public List<CapturedRequest> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Capture the body now: the SDK disposes request content after the send.
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (MaxioBillingService Service, StubHandler Handler) CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = FamilyHandle });
        return (new MaxioBillingService(client, settings, new MemoryCache(new MemoryCacheOptions())), handler);
    }

    private static HttpResponseMessage Route(HttpRequestMessage request,
        string? families = null, string? products = null,
        HttpStatusCode customerLookupStatus = HttpStatusCode.OK, string? customer = null,
        HttpStatusCode subscriptionLookupStatus = HttpStatusCode.OK, string? subscription = null,
        string? createdCustomer = null, string? createdSubscription = null,
        HttpStatusCode createSubscriptionStatus = HttpStatusCode.OK,
        string? customerSubscriptions = null)
    {
        var path = request.RequestUri!.AbsolutePath;
        var method = request.Method;

        if (method == HttpMethod.Get && path == "/product_families.json")
        {
            return Json(HttpStatusCode.OK, families ?? "[]");
        }
        if (method == HttpMethod.Get && path.EndsWith("/products.json"))
        {
            return Json(HttpStatusCode.OK, products ?? "[]");
        }
        if (method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            return customerLookupStatus == HttpStatusCode.OK
                ? Json(HttpStatusCode.OK, customer!)
                : Json(customerLookupStatus, "{}");
        }
        if (method == HttpMethod.Post && path == "/customers.json")
        {
            return Json(HttpStatusCode.Created, createdCustomer!);
        }
        if (method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            return subscriptionLookupStatus == HttpStatusCode.OK
                ? Json(HttpStatusCode.OK, subscription!)
                : Json(subscriptionLookupStatus, "");
        }
        if (method == HttpMethod.Post && path == "/subscriptions.json")
        {
            return Json(createSubscriptionStatus, createdSubscription ?? "");
        }
        if (method == HttpMethod.Get && path.EndsWith("/subscriptions.json"))
        {
            return Json(HttpStatusCode.OK, customerSubscriptions ?? "[]");
        }

        return Json(HttpStatusCode.NotFound, "{}");
    }

    private const string FamiliesJson = """
        [{ "product_family": { "id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe" } }]
        """;

    private const string ProductsJson = """
        [
          { "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "archived_at": null } },
          { "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "archived_at": null } },
          { "product": { "id": 7126959, "name": "Retired Plan", "handle": "retired", "price_in_cents": 100, "interval": 1, "interval_unit": "month", "archived_at": "2026-01-01T00:00:00Z" } }
        ]
        """;

    private const string CustomerJson = """
        { "customer": { "id": 123, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com", "first_name": "Demouser", "last_name": "eShopOnWeb" } }
        """;

    private const string SubscriptionJson = """
        { "subscription": { "id": 555, "reference": "demouser@microsoft.com:eshop-pro", "state": "active", "product_price_in_cents": 29900, "next_assessment_at": "2026-09-25T00:00:00Z", "product": { "handle": "eshop-pro", "name": "Pro Plan" } } }
        """;

    [TestMethod]
    public async Task ListPlansReturnsActivePlansFromConfiguredFamily()
    {
        var (service, handler) = CreateService(req => Route(req, families: FamiliesJson, products: ProductsJson));

        var plans = await service.ListPlansAsync();

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual(7126957, pro.Id);
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);
        // The resolved numeric family id is substituted into the products path.
        Assert.IsTrue(handler.Requests.Any(r => r.Uri.AbsolutePath == "/product_families/3023074/products.json"));
    }

    [TestMethod]
    public async Task SubscribeReturnsExistingSubscriptionWithoutCreatingAnything()
    {
        var (service, handler) = CreateService(req => Route(req,
            customer: CustomerJson,
            subscription: SubscriptionJson));

        var result = await service.SubscribeAsync(Username, "eshop-pro");

        Assert.AreEqual(555, result.Id);
        Assert.AreEqual("active", result.State);
        Assert.AreEqual("eshop-pro", result.ProductHandle);
        Assert.AreEqual(29900, result.PriceInCents);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero), result.NextBillingDate);
        // Idempotency: a repeat subscribe must not issue any create calls.
        Assert.AreEqual(0, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscriptionWhenMissing()
    {
        var (service, handler) = CreateService(req => Route(req,
            customerLookupStatus: HttpStatusCode.NotFound,
            createdCustomer: CustomerJson,
            subscriptionLookupStatus: HttpStatusCode.NotFound,
            createdSubscription: SubscriptionJson));

        var result = await service.SubscribeAsync(Username, "eshop-pro");

        Assert.AreEqual(555, result.Id);
        Assert.AreEqual("active", result.State);

        var createCustomer = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/customers.json");
        StringAssert.Contains(createCustomer.Body!, "\"reference\":\"demouser@microsoft.com\"");

        var createSubscription = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath == "/subscriptions.json");
        StringAssert.Contains(createSubscription.Body!, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(createSubscription.Body!, "\"customer_id\":123");
        StringAssert.Contains(createSubscription.Body!, "\"reference\":\"demouser@microsoft.com:eshop-pro\"");
        StringAssert.Contains(createSubscription.Body!, "\"payment_collection_method\":\"remittance\"");
    }

    [TestMethod]
    public async Task SubscribeSurfacesProviderRejectionAs422()
    {
        var (service, _) = CreateService(req => Route(req,
            customer: CustomerJson,
            subscriptionLookupStatus: HttpStatusCode.NotFound,
            createSubscriptionStatus: HttpStatusCode.UnprocessableEntity,
            createdSubscription: """{ "errors": ["Product: cannot be blank"] }"""));

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(Username, "eshop-pro"));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        StringAssert.Contains(ex.Message, "Product: cannot be blank");
    }

    [TestMethod]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        var (service, handler) = CreateService(req => Route(req,
            customerLookupStatus: HttpStatusCode.NotFound));

        var result = await service.ListMySubscriptionsAsync(Username);

        Assert.AreEqual(0, result.Count);
        Assert.IsFalse(handler.Requests.Any(r => r.Uri.AbsolutePath.EndsWith("/subscriptions.json") && r.Method == HttpMethod.Get && !r.Uri.AbsolutePath.Contains("lookup")));
    }

    [TestMethod]
    public async Task ListMySubscriptionsReturnsMappedSubscriptions()
    {
        var (service, _) = CreateService(req => Route(req,
            customer: CustomerJson,
            customerSubscriptions: $"[{SubscriptionJson}]"));

        var result = await service.ListMySubscriptionsAsync(Username);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(555, result[0].Id);
        Assert.AreEqual("active", result[0].State);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero), result[0].NextBillingDate);
    }
}
