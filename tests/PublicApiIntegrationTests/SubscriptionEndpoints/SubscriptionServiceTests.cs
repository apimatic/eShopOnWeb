using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionServiceTests
{
    private const string Reference = "11111111-1111-1111-1111-111111111111";
    private static readonly MaxioCustomerProfile Shopper = new(Reference, "shopper@example.com", "Test", "Shopper");

    [TestMethod]
    public async Task ListPlans_ReturnsCatalogPlans()
    {
        var server = new FakeMaxioServer();
        var service = server.CreateService();

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.First(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual(1, pro.IntervalCount);
        Assert.AreEqual("month", pro.IntervalUnit);
        Assert.IsTrue(server.Requests.Any(r => r.RequestUri!.AbsolutePath == "/product_families.json"));
        Assert.IsTrue(server.Requests.Any(r => r.RequestUri!.AbsolutePath.EndsWith("/products.json")));
    }

    [TestMethod]
    public async Task ListPlans_FallsBackToDefaultPricePoint_WhenProductHasNoPricing()
    {
        var server = new FakeMaxioServer { OmitProductPricing = true };
        var service = server.CreateService();

        var plans = await service.ListPlansAsync(CancellationToken.None);

        var pro = plans.First(p => p.Handle == "eshop-pro");
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual("month", pro.IntervalUnit);
        Assert.IsTrue(server.Requests.Any(r => r.RequestUri!.AbsolutePath.EndsWith("/price_points.json")));
    }

    [TestMethod]
    public async Task Subscribe_CreatesCustomerAndSubscription()
    {
        var server = new FakeMaxioServer();
        var service = server.CreateService();

        var result = await service.SubscribeAsync(Shopper, "eshop-pro", CancellationToken.None);

        Assert.IsTrue(result.Created);
        Assert.AreEqual("eshop-pro", result.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", result.Subscription.PlanName);
        Assert.AreEqual(29900, result.Subscription.PriceInCents);
        Assert.AreEqual("USD", result.Subscription.Currency);
        Assert.AreEqual("month", result.Subscription.IntervalUnit);
        Assert.AreEqual(1, result.Subscription.IntervalCount);
        Assert.AreEqual("active", result.Subscription.State);
        Assert.IsNotNull(result.Subscription.NextAssessmentAt);
        Assert.IsNotNull(result.Subscription.CurrentPeriodEndsAt);
        Assert.AreEqual(1, server.Customers.Count);
        Assert.AreEqual(1, server.Subscriptions.Count);
        Assert.AreEqual(Reference, server.Customers[0].Reference);
        Assert.IsTrue(server.Requests.Any(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/customers.json"));
        Assert.IsTrue(server.Requests.Any(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/subscriptions.json"));
    }

    [TestMethod]
    public async Task Subscribe_IsIdempotent_WhenAlreadyActive()
    {
        var server = new FakeMaxioServer();
        server.EnsureCustomer(Reference);
        var seeded = server.AddSubscription(Reference, "eshop-pro", state: "active");
        var service = server.CreateService();

        var result = await service.SubscribeAsync(Shopper, "eshop-pro", CancellationToken.None);

        Assert.IsFalse(result.Created);
        Assert.AreEqual(seeded.Id, result.Subscription.Id);
        Assert.AreEqual(1, server.Subscriptions.Count);
        Assert.IsFalse(server.Requests.Any(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/subscriptions.json"));
    }

    [TestMethod]
    public async Task Subscribe_Recovers_WhenMaxioRejectsDuplicateCreate()
    {
        var server = new FakeMaxioServer { InsertSubscriptionThenRejectCreate = true };
        var service = server.CreateService();

        var result = await service.SubscribeAsync(Shopper, "eshop-pro", CancellationToken.None);

        Assert.IsFalse(result.Created);
        Assert.AreEqual(1, server.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", result.Subscription.PlanHandle);
    }

    [TestMethod]
    public async Task Subscribe_EnsuresSingleCustomer_WhenCustomerCreateLosesRace()
    {
        var server = new FakeMaxioServer { InsertCustomerThenRejectCreate = true };
        var service = server.CreateService();

        var result = await service.SubscribeAsync(Shopper, "basic-plan", CancellationToken.None);

        Assert.IsTrue(result.Created);
        Assert.AreEqual(1, server.Customers.Count);
        Assert.AreEqual(1, server.Subscriptions.Count);
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_ThrowsMaxioApiException()
    {
        var server = new FakeMaxioServer();
        var service = server.CreateService();

        var ex = await Assert.ThrowsExceptionAsync<MaxioApiException>(
            () => service.SubscribeAsync(Shopper, "no-such-plan", CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_NotConfigured_ThrowsConfigurationException()
    {
        var server = new FakeMaxioServer();
        var service = server.CreateService(new MaxioOptions
        {
            ApiKey = null,
            Subdomain = "site",
            ProductFamilyHandle = "eshop-subscribe"
        });

        await Assert.ThrowsExceptionAsync<MaxioConfigurationException>(
            () => service.SubscribeAsync(Shopper, "eshop-pro", CancellationToken.None));
    }

    [TestMethod]
    public async Task ListPlans_MissingFamily_ThrowsConfigurationException()
    {
        var server = new FakeMaxioServer();
        var service = server.CreateService(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "missing-family"
        });

        var ex = await Assert.ThrowsExceptionAsync<MaxioConfigurationException>(
            () => service.ListPlansAsync(CancellationToken.None));

        Assert.IsTrue(ex.Message.Contains("missing-family", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ListSubscriptions_Empty_WhenNoCustomerExists()
    {
        var server = new FakeMaxioServer();
        var service = server.CreateService();

        var subscriptions = await service.ListSubscriptionsAsync(Shopper, CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
    }

    [TestMethod]
    public async Task ListSubscriptions_ReturnsAllCustomerSubscriptions()
    {
        var server = new FakeMaxioServer();
        server.EnsureCustomer(Reference);
        server.AddSubscription(Reference, "eshop-pro");
        server.AddSubscription(Reference, "basic-plan");
        var service = server.CreateService();

        var subscriptions = await service.ListSubscriptionsAsync(Shopper, CancellationToken.None);

        Assert.AreEqual(2, subscriptions.Count);
        Assert.IsTrue(subscriptions.Any(s => s.PlanHandle == "eshop-pro" && s.State == "active"));
        Assert.IsTrue(subscriptions.Any(s => s.PlanHandle == "basic-plan"));
    }

    [TestMethod]
    public async Task ListPlans_WhenMaxioUnreachable_ThrowsMaxioUnavailableException()
    {
        var service = CreateServiceOverTransportThatThrows();
        var ex = await Assert.ThrowsExceptionAsync<MaxioUnavailableException>(
            () => service.ListPlansAsync(CancellationToken.None));
        Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
    }

    private static MaxioSubscriptionService CreateServiceOverTransportThatThrows()
    {
        var handler = new ThrowingHandler();
        var client = new MaxioAdvancedBilling.MaxioAdvancedBillingClient(
            new HttpClient(handler),
            new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions());
        return new MaxioSubscriptionService(
            client,
            Microsoft.Extensions.Options.Options.Create(new MaxioOptions
            {
                ApiKey = "key",
                Subdomain = "site",
                ProductFamilyHandle = FakeMaxioServer.FamilyHandle
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MaxioSubscriptionService>.Instance);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("connection reset");
        }
    }
}
