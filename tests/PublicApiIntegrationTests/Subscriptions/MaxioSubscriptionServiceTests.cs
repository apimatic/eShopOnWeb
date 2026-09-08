using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private const string CustomerReference = "demouser@microsoft.com";

    [TestMethod]
    public async Task ListPlansMapsCatalogProducts()
    {
        var handler = new StubHandler(request => Json(HttpStatusCode.OK, StubJson.ProductsWithBasic()));
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(default);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);
        Assert.AreEqual(false, pro.RequiresCreditCard);

        var basic = plans.Single(p => p.Handle == "basic-plan");
        Assert.AreEqual(29.00m, basic.Price);
    }

    [TestMethod]
    public async Task SubscribeCreatesCustomerAndSubscriptionWhenAbsent()
    {
        var handler = new StubHandler(RespondToRoutes(
            lookup: Json(HttpStatusCode.NotFound, string.Empty),
            createCustomer: Json(HttpStatusCode.Created, StubJson.CustomerResponse()),
            listSubscriptions: Json(HttpStatusCode.OK, StubJson.EmptyArray()),
            listProducts: Json(HttpStatusCode.OK, StubJson.ProductsWithBasic()),
            createSubscription: Json(HttpStatusCode.Created, StubJson.SubscriptionResponse())));
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(CustomerReference, "eshop-pro", default);

        Assert.IsFalse(result.AlreadySubscribed);
        Assert.AreEqual(456, result.Subscription.MaxioSubscriptionId);
        Assert.AreEqual("eshop-pro", result.Subscription.ProductHandle);
        Assert.AreEqual("Pro Plan", result.Subscription.ProductName);
        Assert.AreEqual(299.00m, result.Subscription.Price);
        Assert.AreEqual("USD", result.Subscription.Currency);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.IsNotNull(result.Subscription.CurrentPeriodEndsAt);

        // The create-subscription write carries the deterministic reference that makes a retried or
        // repeated request harmless.
        var createIndex = handler.Requests.FindIndex(r => r.Method == HttpMethod.Post
            && r.RequestUri!.AbsolutePath.EndsWith("/subscriptions.json", StringComparison.Ordinal));
        Assert.IsTrue(createIndex >= 0, "Expected a create-subscription request.");
        var sentBody = handler.RequestBodies[createIndex]!;
        StringAssert.Contains(sentBody, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(sentBody, "\"customer_reference\":\"" + CustomerReference + "\"");
        StringAssert.Contains(sentBody, "\"reference\":\"" + CustomerReference + ":eshop-pro\"");
        StringAssert.Contains(sentBody, "\"next_billing_at\":\"");
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentWhenAlreadyEnrolled()
    {
        var handler = new StubHandler(RespondToRoutes(
            lookup: Json(HttpStatusCode.OK, StubJson.CustomerResponse()),
            listSubscriptions: Json(HttpStatusCode.OK, StubJson.SubscriptionsArray())));
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(CustomerReference, "eshop-pro", default);

        Assert.IsTrue(result.AlreadySubscribed);
        Assert.AreEqual(456, result.Subscription.MaxioSubscriptionId);

        // Detection-before-creation: only the lookup and the subscription list ran — no create calls.
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task SubscribeThrows404ForUnknownPlan()
    {
        var handler = new StubHandler(RespondToRoutes(
            lookup: Json(HttpStatusCode.OK, StubJson.CustomerResponse()),
            listSubscriptions: Json(HttpStatusCode.OK, StubJson.EmptyArray()),
            listProducts: Json(HttpStatusCode.OK, StubJson.ProductsWithBasic())));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioSubscriptionException>(
            () => service.SubscribeAsync(CustomerReference, "no-such-plan", default));

        Assert.AreEqual(404, ex.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRefusesPlanThatRequiresCreditCard()
    {
        var handler = new StubHandler(RespondToRoutes(
            lookup: Json(HttpStatusCode.OK, StubJson.CustomerResponse()),
            listSubscriptions: Json(HttpStatusCode.OK, StubJson.EmptyArray()),
            listProducts: Json(HttpStatusCode.OK, StubJson.ProductsWithCardRequiredPlan())));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioSubscriptionException>(
            () => service.SubscribeAsync(CustomerReference, "card-plan", default));

        Assert.AreEqual(400, ex.StatusCode);
        Assert.IsFalse(handler.Requests.Any(r => r.Method == HttpMethod.Post));
    }

    [TestMethod]
    public async Task ListSubscriptionsReturnsEmptyWhenNoCustomerExists()
    {
        var handler = new StubHandler(RespondToRoutes(
            lookup: Json(HttpStatusCode.NotFound, string.Empty)));
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync(CustomerReference, default);

        Assert.AreEqual(0, subscriptions.Count);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ListSubscriptionsMapsCustomerSubscriptions()
    {
        var handler = new StubHandler(RespondToRoutes(
            lookup: Json(HttpStatusCode.OK, StubJson.CustomerResponse()),
            listSubscriptions: Json(HttpStatusCode.OK, StubJson.SubscriptionsArray())));
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync(CustomerReference, default);

        Assert.AreEqual(1, subscriptions.Count);
        var subscription = subscriptions[0];
        Assert.AreEqual("eshop-pro", subscription.ProductHandle);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual(299.00m, subscription.Price);
    }

    [TestMethod]
    public async Task ProviderConnectionFailureBecomesServiceUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioSubscriptionException>(
            () => service.ListPlansAsync(default));

        Assert.AreEqual(503, ex.StatusCode);
    }

    private static MaxioSubscriptionService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler),
            new MaxioAdvancedBillingClientOptions());

        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-5",
            ProductFamilyHandle = "eshop-subscribe"
        };

        return new MaxioSubscriptionService(client, options, NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> RespondToRoutes(
        HttpResponseMessage? lookup = null,
        HttpResponseMessage? createCustomer = null,
        HttpResponseMessage? listSubscriptions = null,
        HttpResponseMessage? listProducts = null,
        HttpResponseMessage? createSubscription = null)
    {
        return request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get && path.Contains("customers/lookup.json", StringComparison.Ordinal))
            {
                return lookup!;
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.Ordinal))
            {
                return createCustomer!;
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return listSubscriptions!;
            }

            if (request.Method == HttpMethod.Get && path.Contains("products.json", StringComparison.Ordinal))
            {
                return listProducts!;
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                return createSubscription!;
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
