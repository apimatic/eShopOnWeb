using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.MaxioBilling;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.MaxioBilling;

/// <summary>
/// Unit tests for the Maxio billing boundary, exercised through the SDK
/// client's HttpClient seam (a stub HttpMessageHandler) — no live Maxio.
/// </summary>
[TestClass]
public class MaxioBillingServiceTests
{
    private static MaxioOptions FakeOptions() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private static (MaxioBillingService Service, FakeMaxioHandler Handler) BuildService()
    {
        var handler = new FakeMaxioHandler();
        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-api-key", Password = "x" },
            Retry = RetryOptions.Default() with
            {
                Timeout = TimeSpan.FromSeconds(5),
                MaxRetries = 1
            }
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), clientOptions);
        var service = new MaxioBillingService(client, FakeOptions(), new NullAppLogger());
        return (service, handler);
    }

    private static UserBillingIdentity FakeUser() =>
        new("user-1", "demouser@microsoft.com", "Demo", "User");

    [TestMethod]
    public async Task ListPlans_FiltersByConfiguredFamilyHandle()
    {
        var (service, _) = BuildService();

        var plans = await service.ListPlansAsync(default);

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual(29900, plans[0].PriceInCents);
        Assert.AreEqual(299m, plans[0].Price);
        Assert.AreEqual("month", plans[0].IntervalUnit);
    }

    [TestMethod]
    public async Task Subscribe_WhenNothingExists_CreatesCustomerAndSubscription()
    {
        var (service, handler) = BuildService();

        var subscription = await service.SubscribeAsync(FakeUser(), "eshop-pro", default);

        Assert.AreEqual(1, handler.CustomerPosts);
        Assert.AreEqual(1, handler.SubscriptionPosts);

        var createRequest = handler.Recorded.Single(r =>
            r.Method == "POST" && r.PathAndQuery.Contains("subscriptions.json"));
        var body = createRequest.Body!;
        StringAssert.Contains(body, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(body, "\"customer_id\":42");
        StringAssert.Contains(body, "\"payment_collection_method\":\"remittance\"");
        StringAssert.Contains(body, MaxioBillingService.SubscriptionReference("user-1", "eshop-pro"));

        Assert.AreEqual(9001, subscription.SubscriptionId);
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual("eshop-pro", subscription.PlanHandle);
        Assert.AreEqual(29900, subscription.PriceInCents);
        Assert.IsNotNull(subscription.NextBillingDate);
    }

    [TestMethod]
    public async Task Subscribe_WhenCustomerAndSubscriptionExist_IsIdempotent()
    {
        var (service, handler) = BuildService();
        handler.CustomerExists = true;
        handler.SubscriptionExists = true;

        var subscription = await service.SubscribeAsync(FakeUser(), "eshop-pro", default);

        Assert.AreEqual(0, handler.CustomerPosts);
        Assert.AreEqual(0, handler.SubscriptionPosts);
        Assert.AreEqual(9001, subscription.SubscriptionId);
        Assert.AreEqual("active", subscription.State);
    }

    [TestMethod]
    public async Task Subscribe_WhenExistingSubscriptionIsTerminal_UsesNextReferenceCandidate()
    {
        var (service, handler) = BuildService();
        handler.CustomerExists = true;

        // The first find (base reference) hits an expired subscription; the
        // next candidate is free, so the create goes out with reference "-2".
        var findCalls = 0;
        handler.Route("GET", "/subscriptions/lookup.json", _ =>
        {
            findCalls++;
            return findCalls == 1
                ? FakeMaxioHandler.Json(HttpStatusCode.OK, handler.BuildSubscription("expired"))
                : FakeMaxioHandler.Raw(HttpStatusCode.NotFound, "{}");
        });

        var subscription = await service.SubscribeAsync(FakeUser(), "eshop-pro", default);

        Assert.AreEqual(1, handler.SubscriptionPosts);
        var createRequest = handler.Recorded.Single(r =>
            r.Method == "POST" && r.PathAndQuery.Contains("subscriptions.json"));
        StringAssert.Contains(createRequest.Body!, $"{MaxioBillingService.SubscriptionReference("user-1", "eshop-pro")}-2");
        Assert.AreEqual(9001, subscription.SubscriptionId);
    }

    [TestMethod]
    public async Task Subscribe_WhenMaxioRejects_Create_Maps422WithMessages()
    {
        var (service, handler) = BuildService();
        handler.CustomerExists = true;
        handler.Route("POST", "/subscriptions.json", _ =>
            FakeMaxioHandler.Raw(HttpStatusCode.UnprocessableEntity, "{\"errors\": [\"Product cannot be subscribed\"]}"));

        var exception = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync(FakeUser(), "eshop-pro", default));

        Assert.AreEqual(422, exception.StatusCode);
        StringAssert.Contains(exception.Message, "Product cannot be subscribed");
    }

    [TestMethod]
    public async Task Subscribe_OnTransportFailure_ReconcilesFromMaxioState()
    {
        var (service, handler) = BuildService();
        handler.CustomerExists = true;
        handler.FailSubscriptionCreateWithTransportError = true;
        // After the (retried) create's unknown outcome, the reconcile pass
        // re-reads Maxio and finds the subscription it created.
        handler.Route("GET", "/subscriptions/lookup.json", _ =>
            handler.SubscriptionPosts == 0
                ? FakeMaxioHandler.Raw(HttpStatusCode.NotFound, "{}")
                : FakeMaxioHandler.Json(HttpStatusCode.OK, handler.BuildSubscription("active")));

        var subscription = await service.SubscribeAsync(FakeUser(), "eshop-pro", default);

        Assert.AreEqual(9001, subscription.SubscriptionId);
        Assert.AreEqual("active", subscription.State);
    }

    [TestMethod]
    public async Task GetSubscriptions_WhenNoMaxioCustomer_ReturnsEmpty()
    {
        var (service, _) = BuildService();

        var subscriptions = await service.GetSubscriptionsForUserAsync(FakeUser(), default);

        Assert.AreEqual(0, subscriptions.Count);
    }

    [TestMethod]
    public async Task GetSubscriptions_WhenCustomerExists_ListsTheirSubscriptions()
    {
        var (service, handler) = BuildService();
        handler.CustomerExists = true;

        var subscriptions = await service.GetSubscriptionsForUserAsync(FakeUser(), default);

        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual(9001, subscriptions[0].SubscriptionId);
        Assert.AreEqual("active", subscriptions[0].State);
    }

    [TestMethod]
    public async Task GetSubscriptions_WhenMaxioUnreachable_Throws503()
    {
        var (service, handler) = BuildService();
        handler.CustomerExists = true;
        handler.Route("GET", "/customers/42/subscriptions.json", _ =>
            throw new HttpRequestException("connection reset"));

        var exception = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.GetSubscriptionsForUserAsync(FakeUser(), default));

        Assert.AreEqual(503, exception.StatusCode);
    }
}

/// <summary>Silences logging in tests.</summary>
public sealed class NullAppLogger : IAppLogger<MaxioBillingService>
{
    public void LogInformation(string message, params object[] args) { }
    public void LogWarning(string message, params object[] args) { }
}
