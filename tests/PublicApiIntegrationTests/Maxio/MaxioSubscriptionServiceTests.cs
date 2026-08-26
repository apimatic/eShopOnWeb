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
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Maxio;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";

    private const string PlansJson = """
        [
          { "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" } },
          { "product": { "id": 7126958, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month" } }
        ]
        """;

    private const string CustomerJson = """
        { "customer": { "id": 987, "reference": "user-1", "email": "demouser@microsoft.com", "first_name": "Demouser", "last_name": "Customer" } }
        """;

    private const string SubscriptionJson = """
        { "subscription": { "id": 555, "state": "active", "product_price_in_cents": 29900,
            "current_period_started_at": "2026-08-26T00:00:00Z", "current_period_ends_at": "2026-09-26T00:00:00Z",
            "product": { "id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" },
            "customer": { "id": 987 }, "reference": "user-1:eshop-pro" } }
        """;

    private static readonly SubscriptionUserContext User =
        new("user-1", "demouser@microsoft.com", "Demouser", "Customer");

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

        public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            Requests.Add((request.Method, request.RequestUri!.ToString(), body));
            return Task.FromResult(_responder(request, body));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static MaxioSubscriptionService ServiceFor(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        });
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = FamilyHandle });
        return new MaxioSubscriptionService(client, settings, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static bool IsPlanList(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get && request.RequestUri!.ToString().Contains("product_families");

    private static bool IsCustomerLookup(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get && request.RequestUri!.ToString().Contains("customers")
        && !request.RequestUri!.ToString().Contains("subscriptions");

    private static bool IsCustomerCreate(HttpRequestMessage request, string? _) =>
        request.Method == HttpMethod.Post && request.RequestUri!.ToString().Contains("customers");

    private static bool IsSubscriptionList(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get && request.RequestUri!.ToString().Contains("subscriptions");

    private static bool IsSubscriptionCreate(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post && request.RequestUri!.ToString().Contains("subscriptions");

    [TestMethod]
    public async Task ListPlans_ReturnsMappedPlans()
    {
        var handler = new StubHandler((request, _) =>
            IsPlanList(request) ? Json(HttpStatusCode.OK, PlansJson) : new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = ServiceFor(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task ListPlans_UnknownFamily_ThrowsNotFound()
    {
        var handler = new StubHandler((request, _) =>
            IsPlanList(request) ? Json(HttpStatusCode.NotFound, "\"not found\"") : new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = ServiceFor(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.ListPlansAsync(CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_ExistingCustomer_CreatesSubscription()
    {
        var handler = new StubHandler((request, _) =>
        {
            if (IsCustomerLookup(request)) return Json(HttpStatusCode.OK, CustomerJson);
            if (IsSubscriptionList(request)) return Json(HttpStatusCode.OK, "[]");
            if (IsSubscriptionCreate(request)) return Json(HttpStatusCode.OK, SubscriptionJson);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = ServiceFor(handler);

        var subscription = await service.SubscribeAsync(User, "eshop-pro", CancellationToken.None);

        Assert.AreEqual(555, subscription.Id);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual("eshop-pro", subscription.ProductHandle);
        Assert.AreEqual(299.00m, subscription.Price);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);

        var create = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Url.Contains("subscriptions"));
        StringAssert.Contains(create.Body!, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(create.Body!, "\"customer_id\":987");
        // No customer create: the customer already existed.
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post && r.Url.Contains("customers")));
    }

    [TestMethod]
    public async Task Subscribe_DoubleClick_ReturnsExistingWithoutCreating()
    {
        var handler = new StubHandler((request, _) =>
        {
            if (IsCustomerLookup(request)) return Json(HttpStatusCode.OK, CustomerJson);
            if (IsSubscriptionList(request)) return Json(HttpStatusCode.OK, $"[{SubscriptionJson}]");
            if (IsSubscriptionCreate(request)) return Json(HttpStatusCode.OK, SubscriptionJson);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = ServiceFor(handler);

        var subscription = await service.SubscribeAsync(User, "eshop-pro", CancellationToken.None);

        Assert.AreEqual(555, subscription.Id);
        Assert.AreEqual("active", subscription.State);
        // The double-click guard: no second subscription was created.
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post && r.Url.Contains("subscriptions")));
    }

    [TestMethod]
    public async Task Subscribe_NewCustomer_CreatesCustomerThenSubscription()
    {
        var handler = new StubHandler((request, _) =>
        {
            if (IsCustomerLookup(request)) return Json(HttpStatusCode.NotFound, "{}");
            if (IsCustomerCreate(request, null)) return Json(HttpStatusCode.OK, CustomerJson);
            if (IsSubscriptionList(request)) return Json(HttpStatusCode.OK, "[]");
            if (IsSubscriptionCreate(request)) return Json(HttpStatusCode.OK, SubscriptionJson);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = ServiceFor(handler);

        var subscription = await service.SubscribeAsync(User, "eshop-pro", CancellationToken.None);

        Assert.AreEqual(555, subscription.Id);
        var customerCreate = handler.Requests.Single(r => r.Method == HttpMethod.Post && r.Url.Contains("customers"));
        StringAssert.Contains(customerCreate.Body!, "\"reference\":\"user-1\"");
        StringAssert.Contains(customerCreate.Body!, "\"email\":\"demouser@microsoft.com\"");
    }

    [TestMethod]
    public async Task Subscribe_CustomerCreateConflict_ReconcilesByReference()
    {
        var lookupCalls = 0;
        var handler = new StubHandler((request, _) =>
        {
            if (IsCustomerLookup(request))
            {
                lookupCalls++;
                // First lookup: absent. Second lookup (after the 422 race): present.
                return lookupCalls == 1 ? Json(HttpStatusCode.NotFound, "{}") : Json(HttpStatusCode.OK, CustomerJson);
            }
            if (IsCustomerCreate(request, null)) return Json(HttpStatusCode.UnprocessableEntity, "{ \"errors\": { \"reference\": [\"has already been taken\"] } }");
            if (IsSubscriptionList(request)) return Json(HttpStatusCode.OK, "[]");
            if (IsSubscriptionCreate(request)) return Json(HttpStatusCode.OK, SubscriptionJson);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = ServiceFor(handler);

        var subscription = await service.SubscribeAsync(User, "eshop-pro", CancellationToken.None);

        Assert.AreEqual(555, subscription.Id);
        Assert.AreEqual(2, lookupCalls);
    }

    [TestMethod]
    public async Task Subscribe_NoPaymentMethodOnFile_RetriesWithRemittance()
    {
        var subscribeCalls = 0;
        var handler = new StubHandler((request, body) =>
        {
            if (IsCustomerLookup(request)) return Json(HttpStatusCode.OK, CustomerJson);
            if (IsSubscriptionList(request)) return Json(HttpStatusCode.OK, "[]");
            if (IsSubscriptionCreate(request))
            {
                subscribeCalls++;
                return subscribeCalls == 1
                    ? Json(HttpStatusCode.UnprocessableEntity, "{ \"errors\": [\"No payment method was on file for the $299.00 balance\"] }")
                    : Json(HttpStatusCode.OK, SubscriptionJson);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = ServiceFor(handler);

        var subscription = await service.SubscribeAsync(User, "eshop-pro", CancellationToken.None);

        Assert.AreEqual(555, subscription.Id);
        Assert.AreEqual(2, subscribeCalls);
        var retry = handler.Requests.Where(r => r.Method == HttpMethod.Post && r.Url.Contains("subscriptions")).Last();
        StringAssert.Contains(retry.Body!, "\"payment_collection_method\":\"remittance\"");
    }

    [TestMethod]
    public async Task ListMySubscriptions_NoCustomer_ReturnsEmpty()
    {
        var handler = new StubHandler((request, _) =>
            IsCustomerLookup(request) ? Json(HttpStatusCode.NotFound, "{}") : new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = ServiceFor(handler);

        var subscriptions = await service.ListMySubscriptionsAsync("user-1", CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
        // A read never creates anything.
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task ListMySubscriptions_ReturnsMappedSubscriptions()
    {
        var handler = new StubHandler((request, _) =>
        {
            if (IsCustomerLookup(request)) return Json(HttpStatusCode.OK, CustomerJson);
            if (IsSubscriptionList(request)) return Json(HttpStatusCode.OK, $"[{SubscriptionJson}]");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var service = ServiceFor(handler);

        var subscriptions = await service.ListMySubscriptionsAsync("user-1", CancellationToken.None);

        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual("eshop-pro", subscriptions[0].ProductHandle);
        Assert.AreEqual("active", subscriptions[0].State);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), subscriptions[0].NextBillingDate);
    }
}
