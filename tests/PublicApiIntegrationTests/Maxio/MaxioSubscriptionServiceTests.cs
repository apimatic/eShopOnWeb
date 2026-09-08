using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Maxio;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private static MaxioSubscriptionService CreateService(StubHandler handler, bool configured = true)
    {
        var options = new MaxioOptions
        {
            ApiKey = configured ? "test-api-key" : null,
            Subdomain = configured ? "cp-exp-5" : null,
            ProductFamilyHandle = configured ? "eshop-subscribe" : null
        };

        var client = MaxioTestClientFactory.CreateClient(handler);
        return new MaxioSubscriptionService(client, Options.Create(options), NullLogger<MaxioSubscriptionService>.Instance);
    }

    private static HttpResponseMessage Unexpected(string key) => throw new InvalidOperationException($"Unexpected request: {key}");

    [TestMethod]
    public async Task ListPlansAsync_MapsFamilyProductsAndSiteCurrency()
    {
        var handler = new StubHandler(key => key switch
        {
            MaxioWireFixtures.ProductsListRequestKey => StubResponses.Ok(MaxioWireFixtures.ProductsJson),
            "GET /site.json" => StubResponses.Ok(MaxioWireFixtures.SiteJson),
            _ => Unexpected(key)
        });
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual("USD", pro.Currency);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);
        Assert.AreEqual(29m, plans.Single(p => p.Handle == "basic-plan").Price);
    }

    [TestMethod]
    public async Task ListPlansAsync_WhenFamilyNotFound_ThrowsBadGateway()
    {
        var handler = new StubHandler(key => key switch
        {
            MaxioWireFixtures.ProductsListRequestKey => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new System.Net.Http.StringContent("not found", System.Text.Encoding.UTF8, "text/plain")
            },
            _ => Unexpected(key)
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioApiException>(() => service.ListPlansAsync(CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeAsync_CreatesCustomerThenSubscription_WhenNoneExist()
    {
        var handler = new StubHandler(key => key switch
        {
            "GET /subscriptions/lookup.json" => StubResponses.NotFound(),
            "GET /customers/lookup.json" => StubResponses.NotFound(),
            "POST /customers.json" => StubResponses.Created(MaxioWireFixtures.CustomerJson),
            "POST /subscriptions.json" => StubResponses.Created(MaxioWireFixtures.SubscriptionJson),
            _ => Unexpected(key)
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.IsTrue(result.Created);
        Assert.AreEqual(MaxioWireFixtures.SubscriptionId, result.Subscription.Id);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.AreEqual("eshop-pro", result.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", result.Subscription.PlanName);
        Assert.AreEqual(299m, result.Subscription.Price);
        Assert.AreEqual("USD", result.Subscription.Currency);
        Assert.IsNotNull(result.Subscription.NextBillingDate);
        Assert.AreEqual(MaxioWireFixtures.SubscriptionReference, result.Subscription.Reference);
        Assert.AreEqual(1, handler.Count("POST /customers.json"));
        Assert.AreEqual(1, handler.Count("POST /subscriptions.json"));
    }

    [TestMethod]
    public async Task SubscribeAsync_WhenAlreadySubscribed_ReturnsExistingWithoutCreating()
    {
        var handler = new StubHandler(key => key switch
        {
            "GET /subscriptions/lookup.json" => StubResponses.Ok(MaxioWireFixtures.SubscriptionJson),
            _ => Unexpected(key)
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.IsFalse(result.Created);
        Assert.AreEqual(MaxioWireFixtures.SubscriptionId, result.Subscription.Id);
        Assert.AreEqual(0, handler.Count("POST /customers.json"));
        Assert.AreEqual(0, handler.Count("POST /subscriptions.json"));
    }

    [TestMethod]
    public async Task SubscribeAsync_WhenCreateRejectedAsDuplicate_ReconcilesToExistingSubscription()
    {
        var subscriptionLookups = 0;
        var handler = new StubHandler(key => key switch
        {
            "GET /subscriptions/lookup.json" => ++subscriptionLookups == 1
                ? StubResponses.NotFound()
                : StubResponses.Ok(MaxioWireFixtures.SubscriptionJson),
            "GET /customers/lookup.json" => StubResponses.NotFound(),
            "POST /customers.json" => StubResponses.Created(MaxioWireFixtures.CustomerJson),
            "POST /subscriptions.json" => StubResponses.Unprocessable("""{ "errors": ["Subscription reference already exists"] }"""),
            _ => Unexpected(key)
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", CancellationToken.None);

        Assert.IsFalse(result.Created);
        Assert.AreEqual(MaxioWireFixtures.SubscriptionId, result.Subscription.Id);
        Assert.AreEqual(1, handler.Count("POST /subscriptions.json"));
        Assert.AreEqual(2, handler.Count("GET /subscriptions/lookup.json"));
    }

    [TestMethod]
    public async Task SubscribeAsync_WhenCreateRejectedForValidation_SurfacesProviderMessage()
    {
        var subscriptionLookups = 0;
        var handler = new StubHandler(key => key switch
        {
            "GET /subscriptions/lookup.json" => ++subscriptionLookups == 1
                ? StubResponses.NotFound()
                : StubResponses.NotFound(),
            "GET /customers/lookup.json" => StubResponses.NotFound(),
            "POST /customers.json" => StubResponses.Created(MaxioWireFixtures.CustomerJson),
            "POST /subscriptions.json" => StubResponses.Unprocessable("""{ "errors": ["No payment method was on file for the $299.00 balance"] }"""),
            _ => Unexpected(key)
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioApiException>(
            () => service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        StringAssert.Contains(ex.Message, "No payment method was on file");
    }

    [TestMethod]
    public async Task ListUserSubscriptionsAsync_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        var handler = new StubHandler(key => key switch
        {
            "GET /customers/lookup.json" => StubResponses.NotFound(),
            _ => Unexpected(key)
        });
        var service = CreateService(handler);

        var subscriptions = await service.ListUserSubscriptionsAsync("demouser@microsoft.com", CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
    }

    [TestMethod]
    public async Task ListUserSubscriptionsAsync_MapsSubscriptionsForExistingCustomer()
    {
        var handler = new StubHandler(key => key switch
        {
            "GET /customers/lookup.json" => StubResponses.Ok(MaxioWireFixtures.CustomerJson),
            MaxioWireFixtures.CustomerSubscriptionsRequestKey => StubResponses.Ok(MaxioWireFixtures.SubscriptionsListJson),
            _ => Unexpected(key)
        });
        var service = CreateService(handler);

        var subscriptions = await service.ListUserSubscriptionsAsync("demouser@microsoft.com", CancellationToken.None);

        Assert.AreEqual(1, subscriptions.Count);
        var subscription = subscriptions[0];
        Assert.AreEqual("active", subscription.State);
        Assert.AreEqual("eshop-pro", subscription.PlanHandle);
        Assert.AreEqual(299m, subscription.Price);
        Assert.IsNotNull(subscription.NextBillingDate);
    }

    [TestMethod]
    public async Task SubscribeAsync_WhenProviderUnreachable_ThrowsBadGateway()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsExceptionAsync<MaxioApiException>(
            () => service.SubscribeAsync("demouser@microsoft.com", "eshop-pro", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
    }

    [TestMethod]
    public async Task AnyCall_WhenNotConfigured_ThrowsConfigurationException()
    {
        var handler = new StubHandler(key => StubResponses.Ok(MaxioWireFixtures.ProductsJson));
        var service = CreateService(handler, configured: false);

        await Assert.ThrowsExceptionAsync<MaxioConfigurationException>(
            () => service.ListPlansAsync(CancellationToken.None));
    }
}
