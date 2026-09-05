using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Maxio;

/// <summary>
/// Exercises the subscribe-flow orchestration (ensure customer, then idempotent enroll)
/// against a fake Maxio client, independent of any live Maxio sandbox.
/// </summary>
[TestClass]
public class MaxioSubscriptionServiceTests
{
    private static MaxioSubscriptionService CreateService(FakeMaxioApiClient client) =>
        new(client,
            Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" }),
            new NoOpAppLogger<MaxioSubscriptionService>());

    private static void SeedProPlan(FakeMaxioApiClient client) =>
        client.Products.Add(new MaxioProduct { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" });

    [TestMethod]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNeitherExist()
    {
        var client = new FakeMaxioApiClient();
        SeedProPlan(client);
        var service = CreateService(client);

        var result = await service.SubscribeAsync("shopper@example.com", "shopper@example.com", "eshop-pro");

        Assert.AreEqual("eshop-pro", result.PlanHandle);
        Assert.AreEqual(1, client.CreateCustomerCallCount);
        Assert.AreEqual(1, client.CreateSubscriptionCallCount);
        Assert.AreEqual("shopper@example.com", client.Customers.Single().Reference);
    }

    [TestMethod]
    public async Task SubscribeAsync_ReusesExistingCustomer_InsteadOfCreatingAnother()
    {
        var client = new FakeMaxioApiClient();
        SeedProPlan(client);
        client.Customers.Add(new MaxioCustomer { Id = 10, Reference = "shopper@example.com", Email = "shopper@example.com" });
        var service = CreateService(client);

        await service.SubscribeAsync("shopper@example.com", "shopper@example.com", "eshop-pro");

        Assert.AreEqual(0, client.CreateCustomerCallCount);
        Assert.AreEqual(1, client.Customers.Count);
    }

    [TestMethod]
    public async Task SubscribeAsync_IsIdempotent_DoubleClickReturnsSameSubscriptionInsteadOfDuplicating()
    {
        var client = new FakeMaxioApiClient();
        SeedProPlan(client);
        var service = CreateService(client);

        var first = await service.SubscribeAsync("shopper@example.com", "shopper@example.com", "eshop-pro");
        var second = await service.SubscribeAsync("shopper@example.com", "shopper@example.com", "eshop-pro");

        Assert.AreEqual(first.MaxioSubscriptionId, second.MaxioSubscriptionId);
        Assert.AreEqual(1, client.CreateCustomerCallCount);
        Assert.AreEqual(1, client.CreateSubscriptionCallCount);
        Assert.AreEqual(1, client.Subscriptions.Count);
    }

    [TestMethod]
    public async Task SubscribeAsync_AllowsSubscribingToADifferentPlan_AfterAnExistingCanceledOne()
    {
        var client = new FakeMaxioApiClient();
        SeedProPlan(client);
        var customer = new MaxioCustomer { Id = 10, Reference = "shopper@example.com", Email = "shopper@example.com" };
        client.Customers.Add(customer);
        client.Subscriptions.Add(new MaxioSubscription
        {
            Id = 999,
            State = "canceled",
            Customer = customer,
            Product = client.Products[0]
        });
        var service = CreateService(client);

        var result = await service.SubscribeAsync("shopper@example.com", "shopper@example.com", "eshop-pro");

        Assert.AreNotEqual(999, result.MaxioSubscriptionId);
        Assert.AreEqual(1, client.CreateSubscriptionCallCount);
    }

    [TestMethod]
    public async Task SubscribeAsync_Throws_WhenPlanHandleIsUnknown()
    {
        var client = new FakeMaxioApiClient();
        SeedProPlan(client);
        var service = CreateService(client);

        await Assert.ThrowsExceptionAsync<MaxioPlanNotFoundException>(
            () => service.SubscribeAsync("shopper@example.com", "shopper@example.com", "does-not-exist"));
    }

    [TestMethod]
    public async Task GetMySubscriptionsAsync_ReturnsEmpty_WhenNoMaxioCustomerExistsYet()
    {
        var client = new FakeMaxioApiClient();
        var service = CreateService(client);

        var result = await service.GetMySubscriptionsAsync("never-subscribed@example.com");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task GetAvailablePlansAsync_ProjectsMaxioProductsToPlanDtos()
    {
        var client = new FakeMaxioApiClient();
        SeedProPlan(client);
        var service = CreateService(client);

        var plans = await service.GetAvailablePlansAsync();

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("Pro Plan", plans[0].Name);
        Assert.AreEqual("$299.00", plans[0].PriceFormatted);
    }
}
