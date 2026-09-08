using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
public class MaxioBillingServiceTests
{
    private static MaxioBillingService CreateService(StubMaxioHandler stub)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(stub),
            new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = "stub-key", Password = "x" }
            });
        return new MaxioBillingService(
            client,
            Options.Create(new MaxioOptions { ProductFamilyHandle = StubMaxioHandler.FamilyHandle }),
            NullLogger<MaxioBillingService>.Instance);
    }

    [TestMethod]
    public async Task GetPlansAsync_ReturnsPlansInConfiguredFamily()
    {
        var stub = new StubMaxioHandler();
        var service = CreateService(stub);

        var plans = await service.GetPlansAsync(CancellationToken.None);

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == StubMaxioHandler.ProHandle);
        Assert.AreEqual(299m, pro.Price);
        Assert.AreEqual(StubMaxioHandler.ProPriceInCents, pro.PriceInCents);
        Assert.AreEqual("month", pro.IntervalUnit);
        Assert.AreEqual(StubMaxioHandler.FamilyHandle, pro.ProductFamilyHandle);
        Assert.IsTrue(stub.Requests.Any(r => r.Path == "/product_families.json"));
        Assert.IsTrue(stub.Requests.Any(r => r.Path.StartsWith($"/product_families/{StubMaxioHandler.FamilyId}/")));
    }

    [TestMethod]
    public async Task GetPlansAsync_UnknownFamily_ThrowsNotFound()
    {
        var stub = new StubMaxioHandler();
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(stub),
            new MaxioAdvancedBillingClientOptions
            {
                BasicAuth = new BasicAuthCredentials { Username = "stub-key", Password = "x" }
            });
        var service = new MaxioBillingService(
            client,
            Options.Create(new MaxioOptions { ProductFamilyHandle = "no-such-family" }),
            NullLogger<MaxioBillingService>.Instance);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.GetPlansAsync(CancellationToken.None));

        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, ex.Status);
    }

    [TestMethod]
    public async Task SubscribeAsync_NewUser_CreatesCustomerAndSubscription_AndConfirms()
    {
        var stub = new StubMaxioHandler();
        var service = CreateService(stub);

        var result = await service.SubscribeAsync("john.doe@example.com", StubMaxioHandler.ProHandle, CancellationToken.None);

        Assert.AreEqual("active", result.State);
        Assert.AreEqual(StubMaxioHandler.ProHandle, result.PlanHandle);
        Assert.AreEqual("Test Pro", result.PlanName);
        Assert.AreEqual(299m, result.Price);
        Assert.IsNotNull(result.NextBillingDateUtc);
        Assert.AreNotEqual(0, result.SubscriptionId);
        Assert.AreNotEqual(0, result.CustomerId);
        Assert.AreEqual(1, stub.CustomerCreates);
        Assert.AreEqual(1, stub.SubscriptionCreates);

        var customerBody = stub.Requests.Single(r => r.Method == "POST" && r.Path == "/customers.json").Body!;
        using (var doc = JsonDocument.Parse(customerBody))
        {
            var customer = doc.RootElement.GetProperty("customer");
            Assert.AreEqual("john.doe@example.com", customer.GetProperty("email").GetString());
            Assert.AreEqual($"eshopweb-user:john.doe@example.com", customer.GetProperty("reference").GetString());
        }

        var subscriptionBody = stub.Requests.Single(r => r.Method == "POST" && r.Path == "/subscriptions.json").Body!;
        using (var doc = JsonDocument.Parse(subscriptionBody))
        {
            var subscription = doc.RootElement.GetProperty("subscription");
            Assert.AreEqual(StubMaxioHandler.ProHandle, subscription.GetProperty("product_handle").GetString());
            Assert.AreEqual("eshopweb-sub:john.doe@example.com:test-pro", subscription.GetProperty("reference").GetString());
            Assert.AreEqual(result.CustomerId, subscription.GetProperty("customer_id").GetInt32());
        }
    }

    [TestMethod]
    public async Task SubscribeAsync_Twice_DoesNotCreateDuplicates()
    {
        var stub = new StubMaxioHandler();
        var service = CreateService(stub);

        var first = await service.SubscribeAsync("demouser@example.com", StubMaxioHandler.ProHandle, CancellationToken.None);
        var second = await service.SubscribeAsync("demouser@example.com", StubMaxioHandler.ProHandle, CancellationToken.None);

        Assert.AreEqual(first.SubscriptionId, second.SubscriptionId);
        Assert.AreEqual(1, stub.CustomerCreates);
        Assert.AreEqual(1, stub.SubscriptionCreates);
    }

    [TestMethod]
    public async Task SubscribeAsync_UnknownPlan_ThrowsUnprocessableEntity()
    {
        var stub = new StubMaxioHandler();
        var service = CreateService(stub);

        var ex = await Assert.ThrowsExceptionAsync<MaxioBillingException>(
            () => service.SubscribeAsync("demouser@example.com", "no-such-plan", CancellationToken.None));

        Assert.AreEqual(System.Net.HttpStatusCode.UnprocessableEntity, ex.Status);
        Assert.AreEqual(0, stub.SubscriptionCreates);
    }

    [TestMethod]
    public async Task SubscribeAsync_ExistingCustomerByEmail_DoesNotRecreateCustomer()
    {
        var stub = new StubMaxioHandler();
        var service = CreateService(stub);
        await service.SubscribeAsync("jane@example.com", StubMaxioHandler.BasicHandle, CancellationToken.None);
        var createsAfterFirst = stub.CustomerCreates;

        var result = await service.SubscribeAsync("jane@example.com", StubMaxioHandler.ProHandle, CancellationToken.None);

        Assert.AreEqual(createsAfterFirst, stub.CustomerCreates);
        Assert.AreEqual("active", result.State);
        Assert.AreEqual(StubMaxioHandler.ProHandle, result.PlanHandle);
    }

    [TestMethod]
    public async Task GetMySubscriptionsAsync_ReturnsSubscriptionsForUser()
    {
        var stub = new StubMaxioHandler();
        var service = CreateService(stub);
        await service.SubscribeAsync("kate@example.com", StubMaxioHandler.ProHandle, CancellationToken.None);

        var subscriptions = await service.GetMySubscriptionsAsync("kate@example.com", CancellationToken.None);

        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual(StubMaxioHandler.ProHandle, subscriptions[0].PlanHandle);
        Assert.AreEqual("active", subscriptions[0].State);
        Assert.IsNotNull(subscriptions[0].NextBillingDateUtc);
    }

    [TestMethod]
    public async Task GetMySubscriptionsAsync_UnknownUser_ReturnsEmptyList_AfterEnsuringCustomer()
    {
        var stub = new StubMaxioHandler();
        var service = CreateService(stub);

        var subscriptions = await service.GetMySubscriptionsAsync("newuser@example.com", CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
        Assert.AreEqual(1, stub.CustomerCreates);
    }
}
