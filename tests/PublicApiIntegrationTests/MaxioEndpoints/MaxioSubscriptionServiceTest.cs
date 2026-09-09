using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.Maxio.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Exercises the Maxio subscription service through the SDK's HttpClient seam with a
/// stubbed provider — no network access.
/// </summary>
[TestClass]
public class MaxioSubscriptionServiceTest
{
    private const string FamiliesJson =
        """[{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]""";

    private const string ProProductJson =
        """{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","description":"Pro tier","price_in_cents":29900,"interval":1,"interval_unit":"month","request_credit_card":false,"require_credit_card":false,"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}}""";

    private const string BasicProductJson =
        """{"product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","description":"Basic tier","price_in_cents":2900,"interval":1,"interval_unit":"month","request_credit_card":false,"require_credit_card":false,"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}}""";

    private const string CustomerJson =
        """{"customer":{"id":5001,"reference":"user-1","email":"user-1@example.com","first_name":"User","last_name":"1"}}""";

    private const string ProSubscriptionJson =
        """{"subscription":{"id":9001,"state":"active","product_price_in_cents":29900,"currency":"USD","current_period_started_at":"2026-09-09T12:00:00-04:00","current_period_ends_at":"2026-10-09T12:00:00-04:00","activated_at":"2026-09-09T12:00:00-04:00","product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"id":3023074,"handle":"eshop-subscribe"}},"customer":{"id":5001,"reference":"user-1"}}}""";

    [TestMethod]
    public async Task GetPlans_MapsCatalogFromConfiguredFamily()
    {
        var handler = new MaxioStubHandler();
        handler.On("GET /product_families.json", HttpStatusCode.OK, FamiliesJson);
        handler.On("GET /product_families/3023074/products.json", HttpStatusCode.OK,
            $"[{ProProductJson},{BasicProductJson}]");
        var service = BuildService(handler);

        var plans = await service.GetPlansAsync(default);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual("per month", pro.Interval);
        Assert.IsFalse(pro.RequestCreditCard);
    }

    [TestMethod]
    public async Task Subscribe_FirstCallCreatesCustomerAndCardlessSubscription()
    {
        var handler = new MaxioStubHandler();
        handler.On("GET /products/handle/eshop-pro.json", HttpStatusCode.OK, ProProductJson);
        handler.On("GET /customers/lookup.json", HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        handler.On("POST /customers.json", HttpStatusCode.Created, CustomerJson);
        handler.On("GET /subscriptions/lookup.json", HttpStatusCode.NotFound, "");
        handler.On("POST /subscriptions.json", HttpStatusCode.Created, ProSubscriptionJson);
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(new MaxioSubscriber("user-1", "user-1@example.com", "user-1@example.com"), "eshop-pro", default);

        Assert.AreEqual(9001, result.Id);
        Assert.AreEqual("active", result.State);
        Assert.AreEqual("eshop-pro", result.PlanHandle);
        Assert.AreEqual("Pro Plan", result.PlanName);
        Assert.AreEqual(299m, result.Price);
        Assert.AreEqual(DateTimeOffset.Parse("2026-10-09T12:00:00-04:00"), result.NextBillingAt);

        var createCustomerBody = await BodyOf(handler, HttpMethod.Post, "/customers.json");
        using (var doc = JsonDocument.Parse(createCustomerBody))
        {
            var customer = doc.RootElement.GetProperty("customer");
            Assert.AreEqual("user-1", customer.GetProperty("reference").GetString());
            Assert.AreEqual("user-1@example.com", customer.GetProperty("email").GetString());
        }

        var createSubscriptionBody = await BodyOf(handler, HttpMethod.Post, "/subscriptions.json");
        using (var doc = JsonDocument.Parse(createSubscriptionBody))
        {
            var subscription = doc.RootElement.GetProperty("subscription");
            Assert.AreEqual("eshop-pro", subscription.GetProperty("product_handle").GetString());
            Assert.AreEqual(5001, subscription.GetProperty("customer_id").GetInt32());
            Assert.AreEqual("user-1:eshop-pro", subscription.GetProperty("reference").GetString());
            Assert.AreEqual("remittance", subscription.GetProperty("payment_collection_method").GetString());
            Assert.IsFalse(subscription.TryGetProperty("payment_profile_id", out _));
            Assert.IsFalse(subscription.TryGetProperty("credit_card_attributes", out _));
            Assert.IsFalse(subscription.TryGetProperty("bank_account_attributes", out _));
        }
    }

    [TestMethod]
    public async Task Subscribe_RepeatedCallReturnsExistingSubscriptionWithoutCreating()
    {
        var handler = new MaxioStubHandler();
        handler.On("GET /products/handle/eshop-pro.json", HttpStatusCode.OK, ProProductJson);
        handler.On("GET /customers/lookup.json", HttpStatusCode.OK, CustomerJson);
        handler.On("GET /subscriptions/lookup.json", HttpStatusCode.OK, ProSubscriptionJson);
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(new MaxioSubscriber("user-1", "user-1@example.com", "user-1@example.com"), "eshop-pro", default);

        Assert.AreEqual(9001, result.Id);
        Assert.AreEqual(0, handler.Count("POST /subscriptions.json"));
        Assert.AreEqual(0, handler.Count("POST /customers.json"));
    }

    [TestMethod]
    public async Task Subscribe_ConcurrentCreateConflictReturnsExistingSubscription()
    {
        var lookupCalls = 0;
        var handler = new MaxioStubHandler();
        handler.On("GET /products/handle/eshop-pro.json", HttpStatusCode.OK, ProProductJson);
        handler.On("GET /customers/lookup.json", HttpStatusCode.OK, CustomerJson);
        handler.On("GET /subscriptions/lookup.json", _ =>
        {
            lookupCalls++;
            return lookupCalls == 1
                ? MaxioStubHandler.JsonResponse(HttpStatusCode.NotFound, "")
                : MaxioStubHandler.JsonResponse(HttpStatusCode.OK, ProSubscriptionJson);
        });
        handler.On("POST /subscriptions.json", HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference has already been taken"]}""");
        var service = BuildService(handler);

        var result = await service.SubscribeAsync(new MaxioSubscriber("user-1", "user-1@example.com", "user-1@example.com"), "eshop-pro", default);

        Assert.AreEqual(9001, result.Id);
        Assert.AreEqual(2, lookupCalls);
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlanSurfacesNotFound()
    {
        var handler = new MaxioStubHandler();
        handler.On("GET /products/handle/does-not-exist.json", HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        var service = BuildService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(new MaxioSubscriber("user-1", "user-1@example.com", "user-1@example.com"), "does-not-exist", default));

        Assert.AreEqual(404, ex.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_PlanOutsideConfiguredFamilyIsRejected()
    {
        var handler = new MaxioStubHandler();
        handler.On("GET /products/handle/other-family-plan.json", HttpStatusCode.OK,
            """{"product":{"id":10,"handle":"other-family-plan","name":"Other","price_in_cents":100,"interval":1,"interval_unit":"month","product_family":{"id":99,"handle":"another-family"}}}""");
        var service = BuildService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(new MaxioSubscriber("user-1", "user-1@example.com", "user-1@example.com"), "other-family-plan", default));

        Assert.AreEqual(400, ex.StatusCode);
    }

    [TestMethod]
    public async Task GetSubscriptionsForUser_WithoutMaxioCustomerIsEmpty()
    {
        var handler = new MaxioStubHandler();
        handler.On("GET /customers/lookup.json", HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        var service = BuildService(handler);

        var subscriptions = await service.GetSubscriptionsForUserAsync("user-1", default);

        Assert.AreEqual(0, subscriptions.Count);
        Assert.AreEqual(0, handler.Count("POST /customers.json"));
    }

    [TestMethod]
    public async Task GetSubscriptionsForUser_ReturnsCustomerSubscriptions()
    {
        var handler = new MaxioStubHandler();
        handler.On("GET /customers/lookup.json", HttpStatusCode.OK, CustomerJson);
        handler.On("GET /customers/5001/subscriptions.json", HttpStatusCode.OK, $"[{ProSubscriptionJson}]");
        var service = BuildService(handler);

        var subscriptions = await service.GetSubscriptionsForUserAsync("user-1", default);

        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual(9001, subscriptions[0].Id);
        Assert.AreEqual("eshop-pro", subscriptions[0].PlanHandle);
        Assert.AreEqual("active", subscriptions[0].State);
    }

    private static MaxioSubscriptionService BuildService(MaxioStubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        return new MaxioSubscriptionService(
            client,
            Options.Create(new MaxioOptions
            {
                ApiKey = "test-key",
                Subdomain = "test",
                ProductFamilyHandle = "eshop-subscribe"
            }),
            NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static async Task<string> BodyOf(MaxioStubHandler handler, HttpMethod method, string path)
    {
        var index = handler.Requests.FindLastIndex(r =>
            r.Method == method && r.RequestUri!.AbsolutePath == path);
        Assert.IsTrue(index >= 0, $"No {method} request to {path} was recorded.");
        await Task.CompletedTask;
        return handler.Bodies[index] ?? string.Empty;
    }
}
