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
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Unit tests for <see cref="MaxioBillingService"/> using the SDK's HttpClient constructor as the test
/// seam (a scripted stub handler) — no network. Wire shapes mirror the live Maxio sandbox responses.
/// </summary>
[TestClass]
public class MaxioBillingServiceTests
{
    private static readonly ShopperIdentity Shopper =
        new("demouser@microsoft.com", "demouser@microsoft.com", "demouser", "Shopper");

    // ---- wire-shape fixtures (snake_case, matching the Maxio API) ----

    private const string CustomerJson =
        """{"customer":{"id":123,"reference":"demouser@microsoft.com","email":"demouser@microsoft.com","first_name":"demouser","last_name":"Shopper"}}""";

    private const string ProductsJson =
        """[{"product":{"id":7130999,"handle":"eshop-pro","name":"Pro Plan","description":null,"price_in_cents":29900,"interval":1,"interval_unit":"month"}},{"product":{"id":7131000,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}]""";

    private const string SubscriptionJson =
        """{"subscription":{"id":93847964,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"},"product_price_in_cents":29900,"current_period_started_at":"2026-08-16T00:05:13+05:00","current_period_ends_at":"2026-09-16T00:05:13+05:00","next_assessment_at":"2026-09-16T00:05:13+05:00"}}""";

    private static string ActiveSubscriptionsList => $"[{SubscriptionJson}]";

    private const string EmptySubscriptionsList = "[]";

    // ---- tests ----

    [TestMethod]
    public async Task ListPlansAsync_MapsProductsToPlans()
    {
        var (svc, _) = BuildService(Ok(ProductsJson));

        var plans = await svc.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual("$299.00", pro.FormattedPrice);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);
        Assert.AreEqual(7130999, pro.ProductId);
    }

    [TestMethod]
    public async Task SubscribeAsync_CreatesSubscription_WhenCustomerExistsAndNoActiveSubscription()
    {
        // read customer (found) -> list subs (empty) -> create subscription
        var (svc, handler) = BuildService(Ok(CustomerJson), Ok(EmptySubscriptionsList), Ok(SubscriptionJson));

        var result = await svc.SubscribeAsync(Shopper, "eshop-pro", CancellationToken.None);

        Assert.IsFalse(result.AlreadySubscribed);
        Assert.AreEqual(93847964, result.Subscription.Id);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual("eshop-pro", result.Subscription.ProductHandle);
        Assert.AreEqual("$299.00", result.Subscription.FormattedPrice);

        // The subscription create must carry the plan handle and enroll on a remittance basis (no card).
        var createBody = handler.RequestBodies.Last();
        StringAssert.Contains(createBody, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(createBody, "\"payment_collection_method\":\"remittance\"");
        Assert.AreEqual(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task SubscribeAsync_IsIdempotent_WhenActiveSubscriptionAlreadyExists()
    {
        // read customer (found) -> list subs (already has an active eshop-pro sub): must NOT create another
        var (svc, handler) = BuildService(Ok(CustomerJson), Ok(ActiveSubscriptionsList));

        var result = await svc.SubscribeAsync(Shopper, "eshop-pro", CancellationToken.None);

        Assert.IsTrue(result.AlreadySubscribed);
        Assert.AreEqual(93847964, result.Subscription.Id);
        Assert.AreEqual(0, handler.Requests.Count(r => r.Method == HttpMethod.Post),
            "No POST (create) should be sent when an active subscription already exists.");
    }

    [TestMethod]
    public async Task SubscribeAsync_CreatesCustomer_WhenCustomerNotFound()
    {
        // read customer 404 -> create customer -> list subs (empty) -> create subscription
        var (svc, handler) = BuildService(
            Status(HttpStatusCode.NotFound, """{"errors":["not found"]}"""),
            Ok(CustomerJson),
            Ok(EmptySubscriptionsList),
            Ok(SubscriptionJson));

        var result = await svc.SubscribeAsync(Shopper, "eshop-pro", CancellationToken.None);

        Assert.IsFalse(result.AlreadySubscribed);
        Assert.AreEqual(93847964, result.Subscription.Id);
        // One POST creates the customer, one POST creates the subscription.
        Assert.AreEqual(2, handler.Requests.Count(r => r.Method == HttpMethod.Post));
        var customerCreate = handler.RequestBodies.First(b => b.Contains("\"reference\""));
        StringAssert.Contains(customerCreate, "\"reference\":\"demouser@microsoft.com\"");
    }

    [TestMethod]
    public async Task SubscribeAsync_BlankPlanHandle_ThrowsBadRequest()
    {
        var (svc, handler) = BuildService();

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => svc.SubscribeAsync(Shopper, "  ", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.AreEqual(0, handler.Requests.Count, "No SDK call should be made for a blank plan handle.");
    }

    [TestMethod]
    public async Task ListPlansAsync_NotConfigured_ThrowsServerError()
    {
        var handler = new SequenceHandler();
        var client = BuildClient(handler);
        var settings = Options.Create(new MaxioSettings()); // all blank
        var svc = new MaxioBillingService(client, settings, NullLogger<MaxioBillingService>.Instance);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => svc.ListPlansAsync(CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ListMySubscriptionsAsync_ReturnsEmpty_WhenNoCustomer()
    {
        var (svc, _) = BuildService(Status(HttpStatusCode.NotFound, """{"errors":["not found"]}"""));

        var subs = await svc.ListMySubscriptionsAsync(Shopper, CancellationToken.None);

        Assert.AreEqual(0, subs.Count);
    }

    [TestMethod]
    public async Task ListPlansAsync_ProviderServerError_ThrowsBadGateway()
    {
        // Always 500 (survives the one retry) — a provider outage must surface as 502, not 500/200.
        var handler = new SequenceHandler();
        handler.Fallback = _ => Json(HttpStatusCode.InternalServerError, """{"error":"boom"}""");
        var svc = new MaxioBillingService(BuildClient(handler),
            Options.Create(ValidSettings()), NullLogger<MaxioBillingService>.Instance);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => svc.ListPlansAsync(CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
    }

    // ---- helpers ----

    private static MaxioSettings ValidSettings() =>
        new() { ApiKey = "k", Subdomain = "test", ProductFamilyHandle = "eshop-subscribe" };

    private static (MaxioBillingService svc, SequenceHandler handler) BuildService(
        params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
    {
        var handler = new SequenceHandler(responders);
        var svc = new MaxioBillingService(BuildClient(handler),
            Options.Create(ValidSettings()), NullLogger<MaxioBillingService>.Instance);
        return (svc, handler);
    }

    private static MaxioAdvancedBillingClient BuildClient(SequenceHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "k", Password = "x" },
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with { MaxRetries = 1 }
        };
        options.Server.Production.Us.Site = "test";
        return new MaxioAdvancedBillingClient(new HttpClient(handler), options);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Ok(string json) =>
        _ => Json(HttpStatusCode.OK, json);

    private static Func<HttpRequestMessage, HttpResponseMessage> Status(HttpStatusCode code, string json) =>
        _ => Json(code, json);

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        /// <summary>Used once the scripted queue is exhausted (e.g. an always-error responder).</summary>
        public Func<HttpRequestMessage, HttpResponseMessage> Fallback { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders) =>
            _responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responders);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            var responder = _responders.Count > 0 ? _responders.Dequeue() : Fallback;
            return responder(request);
        }
    }
}
