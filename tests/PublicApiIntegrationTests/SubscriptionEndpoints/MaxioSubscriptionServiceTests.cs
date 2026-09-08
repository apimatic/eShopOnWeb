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
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Tests for <see cref="MaxioSubscriptionService"/> driven through a stubbed HttpClient, so no
/// real Maxio traffic is involved. The stub speaks the same JSON shapes Maxio returns (captured
/// against the sandbox during development).
/// </summary>
[TestClass]
public class MaxioSubscriptionServiceTests
{
    private static readonly MaxioOptions TestOptions = new()
    {
        ApiKey = "test-key",
        Subdomain = "test",
        ProductFamilyHandle = "eshop-subscribe"
    };

    // ---- fixtures (wire shapes verified against the sandbox) --------------------------------------

    private const string SiteJson = """{ "site": { "currency": "USD" } }""";

    private const string ProductsJson = """
        [
          { "product": { "id": 7155139, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "archived_at": null } },
          { "product": { "id": 7155140, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "archived_at": null } }
        ]
        """;

    private const string CustomerJson =
        """{ "customer": { "id": 12345, "first_name": "Jane", "last_name": "Doe", "email": "jane@example.com", "reference": "usr-1" } }""";

    private static string SubscriptionJson(int id, string handle, string name, string state = "active", int priceInCents = 2900) =>
        $$"""
        { "subscription": {
            "id": {{id}},
            "state": "{{state}}",
            "current_period_started_at": "2026-09-01T00:00:00Z",
            "current_period_ends_at": "2026-10-01T00:00:00Z",
            "next_assessment_at": "2026-10-01T00:00:00Z",
            "product_price_in_cents": {{priceInCents}},
            "currency": "USD",
            "product": { "id": 1, "handle": "{{handle}}", "name": "{{name}}" }
        } }
        """;

    private static string CustomerListJson(params string[] subscriptionJsons) =>
        "[" + string.Join(",", subscriptionJsons) + "]";

    // ---- tests ------------------------------------------------------------------------------------

    [TestMethod]
    public async Task GetPlansAsync_ReturnsCatalogWithPricesAndCurrency()
    {
        var handler = NewHandler(request =>
        {
            if (IsGet(request, "/site.json")) return Json(HttpStatusCode.OK, SiteJson);
            if (IsGet(request, "products.json")) return Json(HttpStatusCode.OK, ProductsJson);
            return Json(HttpStatusCode.NotFound, "");
        });

        var service = BuildService(handler);
        var shopper = Shopper();

        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(29900L, pro.PriceInCents);
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual("USD", pro.Currency);
        var basic = plans.Single(p => p.Handle == "basic-plan");
        Assert.AreEqual(29m, basic.Price);
        Assert.AreEqual(0, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        var handler = NewHandler(request =>
        {
            if (IsGet(request, "/customers/lookup.json")) return Json(HttpStatusCode.NotFound, "");
            if (IsPost(request, "/customers.json")) return Json(HttpStatusCode.Created, CustomerJson);
            if (IsGet(request, "/subscriptions.json")) return Json(HttpStatusCode.OK, "[]");
            if (IsPost(request, "/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson(777, "basic-plan", "Basic Plan"));
            return Json(HttpStatusCode.NotFound, "");
        });

        var service = BuildService(handler);
        var shopper = Shopper();

        var enrollment = await service.SubscribeAsync(shopper, "basic-plan", CancellationToken.None);

        Assert.IsTrue(enrollment.CreatedNew);
        Assert.AreEqual(777, enrollment.Subscription.Id);
        Assert.AreEqual("basic-plan", enrollment.Subscription.PlanHandle);
        Assert.AreEqual("Basic Plan", enrollment.Subscription.PlanName);
        Assert.AreEqual("active", enrollment.Subscription.State);
        Assert.AreEqual(2900L, enrollment.Subscription.PriceInCents);
        Assert.AreEqual("USD", enrollment.Subscription.Currency);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), enrollment.Subscription.NextBillingDate);

        Assert.AreEqual(1, handler.Requests.Count(r => IsPost(r, "/customers.json")));
        Assert.AreEqual(1, handler.Requests.Count(r => IsPost(r, "/subscriptions.json")));

        var createBody = await handler.BodyOfAsync(handler.Requests.Single(r => IsPost(r, "/subscriptions.json")));
        Assert.IsTrue(createBody!.Contains("\"payment_collection_method\":\"remittance\"", StringComparison.Ordinal));
        Assert.IsTrue(createBody.Contains("\"product_handle\":\"basic-plan\"", StringComparison.Ordinal));
        Assert.IsTrue(createBody.Contains("\"customer_id\":12345", StringComparison.Ordinal));

        var customerCreateBody = await handler.BodyOfAsync(handler.Requests.Single(r => IsPost(r, "/customers.json")));
        Assert.IsTrue(customerCreateBody!.Contains("\"reference\":\"usr-1\"", StringComparison.Ordinal));
        // Names are derived from the email when the caller does not provide them (Maxio requires non-blank first/last).
        Assert.IsTrue(customerCreateBody.Contains("\"first_name\":\"jane\"", StringComparison.Ordinal));
        Assert.IsTrue(customerCreateBody.Contains("\"last_name\":\"example.com\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SubscribeAsync_ReturnsExistingSubscriptionWithoutCreatingDuplicate()
    {
        var handler = NewHandler(request =>
        {
            if (IsGet(request, "/customers/lookup.json")) return Json(HttpStatusCode.OK, CustomerJson);
            if (IsGet(request, "/subscriptions.json")) return Json(HttpStatusCode.OK, CustomerListJson(SubscriptionJson(777, "basic-plan", "Basic Plan")));
            return Json(HttpStatusCode.NotFound, "");
        });

        var service = BuildService(handler);
        var shopper = Shopper();

        var enrollment = await service.SubscribeAsync(shopper, "basic-plan", CancellationToken.None);

        Assert.IsFalse(enrollment.CreatedNew);
        Assert.AreEqual(777, enrollment.Subscription.Id);
        Assert.AreEqual(0, handler.Requests.Count(r => r.Method == HttpMethod.Post), "No create calls should be made when a subscription already exists.");
    }

    [TestMethod]
    public async Task SubscribeAsync_IsIdempotentForSamePlanOnSameService()
    {
        StubHandler handler = null!;
        handler = NewHandler(request =>
        {
            if (IsGet(request, "/customers/lookup.json")) return Json(HttpStatusCode.NotFound, "");
            if (IsPost(request, "/customers.json")) return Json(HttpStatusCode.Created, CustomerJson);
            if (IsGet(request, "/subscriptions.json"))
            {
                // First call lists before creating; the second call must see the created one.
                return handler.Requests.Count(r => IsGet(r, "/subscriptions.json")) <= 1
                    ? Json(HttpStatusCode.OK, "[]")
                    : Json(HttpStatusCode.OK, CustomerListJson(SubscriptionJson(777, "basic-plan", "Basic Plan")));
            }
            if (IsPost(request, "/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson(777, "basic-plan", "Basic Plan"));
            return Json(HttpStatusCode.NotFound, "");
        });

        var service = BuildService(handler);
        var shopper = Shopper();

        var first = await service.SubscribeAsync(shopper, "basic-plan", CancellationToken.None);
        var second = await service.SubscribeAsync(shopper, "basic-plan", CancellationToken.None);

        Assert.IsTrue(first.CreatedNew);
        Assert.IsFalse(second.CreatedNew);
        Assert.AreEqual(first.Subscription.Id, second.Subscription.Id);
        Assert.AreEqual(1, handler.Requests.Count(r => IsPost(r, "/customers.json")));
        Assert.AreEqual(1, handler.Requests.Count(r => IsPost(r, "/subscriptions.json")));
    }

    [TestMethod]
    public async Task SubscribeAsync_ReconcilesCustomerWhenCreateResponseIsAmbiguous()
    {
        // Simulates the Maxio behaviour measured on the sandbox: a conflicting/duplicate customer
        // create returns 422 with a body the generated error model cannot parse, which the SDK
        // surfaces as a JsonException (status lost). The service must settle the outcome by
        // re-reading the customer by reference and continuing with the winner.
        StubHandler handler = null!;
        handler = NewHandler(request =>
        {
            if (IsGet(request, "/customers/lookup.json"))
            {
                // First lookup (before create): nobody. Second lookup (reconcile): the winner.
                return handler.Requests.Count(r => IsGet(r, "/customers/lookup.json")) <= 1
                    ? Json(HttpStatusCode.NotFound, "")
                    : Json(HttpStatusCode.OK, CustomerJson);
            }
            if (IsPost(request, "/customers.json")) return Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Reference: must be unique - that value has been taken."] }""");
            if (IsGet(request, "/subscriptions.json")) return Json(HttpStatusCode.OK, "[]");
            if (IsPost(request, "/subscriptions.json")) return Json(HttpStatusCode.Created, SubscriptionJson(777, "basic-plan", "Basic Plan"));
            return Json(HttpStatusCode.NotFound, "");
        });

        var service = BuildService(handler);
        var shopper = Shopper();

        var enrollment = await service.SubscribeAsync(shopper, "basic-plan", CancellationToken.None);

        Assert.IsTrue(enrollment.CreatedNew);
        Assert.AreEqual(777, enrollment.Subscription.Id);
        Assert.AreEqual(1, handler.Requests.Count(r => IsPost(r, "/customers.json")), "A lost/ambiguous create must not be blindly retried as a second create.");
    }

    [TestMethod]
    public async Task SubscribeAsync_ReconcilesSubscriptionWhenCreateOutcomeUnknown()
    {
        // Transport failure after the create request left: the single-send guard refuses the SDK's
        // automatic re-send, and the service reconciles by re-reading the subscription list.
        StubHandler handler = null!;
        handler = NewHandler(request =>
        {
            if (IsGet(request, "/customers/lookup.json")) return Json(HttpStatusCode.NotFound, "");
            if (IsPost(request, "/customers.json")) return Json(HttpStatusCode.Created, CustomerJson);
            if (IsGet(request, "/subscriptions.json"))
            {
                // First list (pre-create): empty. Second list (reconcile): the create had landed.
                return handler.Requests.Count(r => IsGet(r, "/subscriptions.json")) <= 1
                    ? Json(HttpStatusCode.OK, "[]")
                    : Json(HttpStatusCode.OK, CustomerListJson(SubscriptionJson(777, "basic-plan", "Basic Plan")));
            }
            if (IsPost(request, "/subscriptions.json")) throw new HttpRequestException("connection reset");
            return Json(HttpStatusCode.NotFound, "");
        });

        var service = BuildService(handler);
        var shopper = Shopper();

        var enrollment = await service.SubscribeAsync(shopper, "basic-plan", CancellationToken.None);

        Assert.IsTrue(enrollment.CreatedNew);
        Assert.AreEqual(777, enrollment.Subscription.Id);
        Assert.AreEqual(1, handler.Requests.Count(r => IsPost(r, "/subscriptions.json")), "The single-send guard must stop the SDK re-sending the create.");
    }

    [TestMethod]
    public async Task SubscribeAsync_SurfacesRejectedPlanAsClientError()
    {
        var handler = NewHandler(request =>
        {
            if (IsGet(request, "/customers/lookup.json")) return Json(HttpStatusCode.NotFound, "");
            if (IsPost(request, "/customers.json")) return Json(HttpStatusCode.Created, CustomerJson);
            if (IsGet(request, "/subscriptions.json")) return Json(HttpStatusCode.OK, "[]");
            if (IsPost(request, "/subscriptions.json")) return Json(HttpStatusCode.UnprocessableEntity, """{ "errors": ["Product handle not found"] }""");
            return Json(HttpStatusCode.NotFound, "");
        });

        var service = BuildService(handler);
        var shopper = Shopper();

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(shopper, "does-not-exist", CancellationToken.None));

        Assert.AreEqual(StatusCodes.Status400BadRequest, ex.StatusCode);
        StringAssert.Contains(ex.Message, "Product handle not found");
    }

    [TestMethod]
    public async Task GetMySubscriptionsAsync_WhenNeverSubscribed_ReturnsEmpty()
    {
        var handler = NewHandler(request =>
        {
            if (IsGet(request, "/customers/lookup.json")) return Json(HttpStatusCode.NotFound, "");
            return Json(HttpStatusCode.NotFound, "");
        });

        var service = BuildService(handler);
        var shopper = Shopper();

        var subscriptions = await service.GetMySubscriptionsAsync(shopper, CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
    }

    [TestMethod]
    public async Task GetMySubscriptionsAsync_ListsAllCustomerSubscriptions()
    {
        var handler = NewHandler(request =>
        {
            if (IsGet(request, "/customers/lookup.json")) return Json(HttpStatusCode.OK, CustomerJson);
            if (IsGet(request, "/subscriptions.json")) return Json(HttpStatusCode.OK,
                CustomerListJson(
                    SubscriptionJson(777, "basic-plan", "Basic Plan"),
                    SubscriptionJson(778, "eshop-pro", "Pro Plan", priceInCents: 29900)));
            return Json(HttpStatusCode.NotFound, "");
        });

        var service = BuildService(handler);
        var shopper = Shopper();

        var subscriptions = await service.GetMySubscriptionsAsync(shopper, CancellationToken.None);

        Assert.AreEqual(2, subscriptions.Count);
        Assert.AreEqual("basic-plan", subscriptions[0].PlanHandle);
        Assert.AreEqual("eshop-pro", subscriptions[1].PlanHandle);
        Assert.AreEqual(29900L, subscriptions[1].PriceInCents);
    }

    [TestMethod]
    public async Task SubscribeAsync_WhenNotConfigured_ThrowsConfigurationError()
    {
        var handler = NewHandler(_ => Json(HttpStatusCode.NotFound, ""));
        var service = BuildService(handler, new MaxioOptions { ApiKey = "", Subdomain = "", ProductFamilyHandle = "" });

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(Shopper(), "basic-plan", CancellationToken.None));

        Assert.AreEqual(StatusCodes.Status500InternalServerError, ex.StatusCode);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static MaxioShopper Shopper() => new("usr-1", "jane@example.com");

    private static MaxioSubscriptionService BuildService(StubHandler handler, MaxioOptions? options = null)
    {
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Retry = RetryOptions.Default() with { MaxRetries = 1, Timeout = TimeSpan.FromSeconds(10) }
        };
        clientOptions.Server.Production.Us.Site = "test";

        // Mirror the production handler chain: the single-send guard sits in front of the transport.
        var guard = new MaxioSingleSendHandler { InnerHandler = handler };
        var httpClient = new HttpClient(guard) { Timeout = TimeSpan.FromSeconds(15) };
        var client = new MaxioAdvancedBillingClient(httpClient, clientOptions);

        return new MaxioSubscriptionService(client, options ?? TestOptions, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static StubHandler NewHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => new(responder);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static bool IsGet(HttpRequestMessage request, string pathPart) =>
        request.Method == HttpMethod.Get && (request.RequestUri?.AbsolutePath.Contains(pathPart, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsPost(HttpRequestMessage request, string pathPart) =>
        request.Method == HttpMethod.Post && (request.RequestUri?.AbsolutePath.Contains(pathPart, StringComparison.OrdinalIgnoreCase) ?? false);

    internal sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        private readonly Dictionary<HttpRequestMessage, string?> _bodies = new();

        public async Task<string?> BodyOfAsync(HttpRequestMessage request)
        {
            return _bodies.TryGetValue(request, out var body) ? body : null;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                // Capture the body now: the SDK disposes the content after the send.
                _bodies[request] = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return _responder(request);
        }
    }
}
