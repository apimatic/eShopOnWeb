using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.MaxioEndpoints;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private static MaxioOptions TestOptions() => new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private static MaxioSubscriptionService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler),
            new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us,
                BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
            });
        return new MaxioSubscriptionService(client, Options.Create(TestOptions()), NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static bool Is(HttpRequestMessage req, string method, string pathContains) =>
        req.Method.Method.Equals(method, StringComparison.OrdinalIgnoreCase) &&
        req.RequestUri!.AbsolutePath.Contains(pathContains, StringComparison.OrdinalIgnoreCase);

    [TestMethod]
    public async Task GetPlansAsync_ReturnsMappedPlans()
    {
var handler = new StubHandler(req =>
        {
            if (Is(req, "GET", "/products"))
            {
                return Json(HttpStatusCode.OK,
                    """[{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null}},{"product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null}}]""");
            }
            if (Is(req, "GET", "/product_families"))
            {
                return Json(HttpStatusCode.OK,
                    """[{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]""");
            }
            throw new InvalidOperationException($"Unhandled request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);
        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);
        Assert.IsFalse(pro.RequireCreditCard);

        var basic = plans.Single(p => p.Handle == "basic-plan");
        Assert.AreEqual(29.00m, basic.Price);
    }

    [TestMethod]
    public async Task GetPlansAsync_ThrowsNotFound_WhenFamilyMissing()
    {
        var handler = new StubHandler(req =>
        {
            if (Is(req, "GET", "/product_families"))
            {
                return Json(HttpStatusCode.OK,
                    """[{"product_family":{"id":1,"handle":"other-family","name":"Other"}}]""");
            }
            throw new InvalidOperationException($"Unhandled request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);
        var ex = await Assert.ThrowsExceptionAsync<MaxioException>(() => service.GetPlansAsync(CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var handler = new StubHandler(req =>
        {
            if (Is(req, "GET", "/customers/lookup"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }
            if (Is(req, "POST", "/customers"))
            {
                return Json(HttpStatusCode.OK,
                    """{"customer":{"id":1001,"reference":"eshop-demouser@microsoft.com","first_name":"demouser@microsoft.com","last_name":"demouser@microsoft.com","email":"demouser@microsoft.com"}}""");
            }
            if (Is(req, "GET", "/products"))
            {
                return Json(HttpStatusCode.OK,
                    """{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null}}""");
            }
            if (Is(req, "GET", "/subscriptions/lookup"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (Is(req, "POST", "/subscriptions"))
            {
                return Json(HttpStatusCode.OK,
                    """{"subscription":{"id":5001,"reference":"eshop-demouser@microsoft.com-eshop-pro","state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-11-06T00:00:00Z","current_period_ends_at":"2026-11-06T00:00:00Z","product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}""");
            }
            throw new InvalidOperationException($"Unhandled request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);
        var subscription = await service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.AreEqual(5001, subscription.Id);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual("eshop-pro", subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", subscription.PlanName);
        Assert.AreEqual(299.00m, subscription.Price);
        Assert.IsNotNull(subscription.NextBillingDate);

        Assert.AreEqual(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/customers")));
        Assert.AreEqual(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("/subscriptions")));
    }

    [TestMethod]
    public async Task SubscribeAsync_IsIdempotent_WhenSubscriptionAlreadyExists()
    {
        var handler = new StubHandler(req =>
        {
            if (Is(req, "GET", "/customers/lookup"))
            {
                return Json(HttpStatusCode.OK,
                    """{"customer":{"id":1001,"reference":"eshop-demouser@microsoft.com","first_name":"demouser@microsoft.com","last_name":"demouser@microsoft.com","email":"demouser@microsoft.com"}}""");
            }
            if (Is(req, "GET", "/products"))
            {
                return Json(HttpStatusCode.OK,
                    """{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null}}""");
            }
            if (Is(req, "GET", "/subscriptions/lookup"))
            {
                return Json(HttpStatusCode.OK,
                    """{"subscription":{"id":5001,"reference":"eshop-demouser@microsoft.com-eshop-pro","state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-11-06T00:00:00Z","current_period_ends_at":"2026-11-06T00:00:00Z","product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}""");
            }
            throw new InvalidOperationException($"Unhandled request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);
        var subscription = await service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.AreEqual(5001, subscription.Id);
        Assert.AreEqual(0, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task SubscribeAsync_ThrowsNotFound_WhenPlanUnknown()
    {
        var handler = new StubHandler(req =>
        {
            if (Is(req, "GET", "/customers/lookup"))
            {
                return Json(HttpStatusCode.OK,
                    """{"customer":{"id":1001,"reference":"eshop-demouser@microsoft.com","first_name":"demouser@microsoft.com","last_name":"demouser@microsoft.com","email":"demouser@microsoft.com"}}""");
            }
            if (Is(req, "GET", "/products"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }
            throw new InvalidOperationException($"Unhandled request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);
        var ex = await Assert.ThrowsExceptionAsync<MaxioException>(
            () => service.SubscribeAsync("demouser@microsoft.com", "no-such-plan", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [TestMethod]
    public async Task GetSubscriptionsAsync_ReturnsEmpty_WhenNoCustomer()
    {
        var handler = new StubHandler(req =>
        {
            if (Is(req, "GET", "/customers/lookup"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }
            throw new InvalidOperationException($"Unhandled request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);
        var subscriptions = await service.GetSubscriptionsAsync("demouser@microsoft.com", CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
    }

    [TestMethod]
    public async Task GetSubscriptionsAsync_ReturnsMappedSubscriptions()
    {
        var handler = new StubHandler(req =>
        {
            if (Is(req, "GET", "/customers/lookup"))
            {
                return Json(HttpStatusCode.OK,
                    """{"customer":{"id":1001,"reference":"eshop-demouser@microsoft.com","first_name":"demouser@microsoft.com","last_name":"demouser@microsoft.com","email":"demouser@microsoft.com"}}""");
            }
            if (Is(req, "GET", "/subscriptions"))
            {
                return Json(HttpStatusCode.OK,
                    """[{"subscription":{"id":5001,"reference":"eshop-demouser@microsoft.com-eshop-pro","state":"active","product_price_in_cents":29900,"next_assessment_at":"2026-11-06T00:00:00Z","current_period_ends_at":"2026-11-06T00:00:00Z","product":{"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}]""");
            }
            throw new InvalidOperationException($"Unhandled request: {req.Method} {req.RequestUri}");
        });

        var service = CreateService(handler);
        var subscriptions = await service.GetSubscriptionsAsync("demouser@microsoft.com", CancellationToken.None);

        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual("eshop-pro", subscriptions[0].PlanHandle);
        Assert.AreEqual(299.00m, subscriptions[0].Price);
        Assert.AreEqual("active", subscriptions[0].State);
    }
}
