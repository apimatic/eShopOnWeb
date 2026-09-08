using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Maxio;

[TestClass]
public class MaxioSubscriptionServiceTest
{
    private const string ProHandle = "eshop-pro";

    private static readonly SubscriberProfile DemoUser = new("user-1", "demo@example.com", "Demo", "User");

    [TestMethod]
    public async Task ListPlansAsync_ReturnsMappedPlansFromFamily()
    {
        var handler = new FakeMaxioHandler();
        var service = CreateService(handler);

        var plans = (await service.ListPlansAsync(default)).ToList();

        Assert.AreEqual(2, plans.Count);
        var pro = plans.Single(p => p.Handle == ProHandle);
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual("USD", pro.Currency);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);
    }

    [TestMethod]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        var handler = new FakeMaxioHandler();
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(DemoUser, ProHandle, default);

        Assert.IsTrue(result.Created);
        Assert.IsNotNull(result.Subscription);
        Assert.AreEqual("active", result.Subscription!.State);
        Assert.AreEqual(ProHandle, result.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", result.Subscription.PlanName);
        Assert.AreEqual(299.00m, result.Subscription.Price);
        Assert.AreEqual("USD", result.Subscription.Currency);
        Assert.IsTrue(result.Subscription.NextBillingDate.HasValue);
        Assert.AreEqual(1, handler.CustomerPosts);
        Assert.AreEqual(1, handler.SubscriptionPosts);
        Assert.AreEqual(1, handler.Customers.Count);
        Assert.AreEqual("user-1", handler.Customers[0].Reference);
        Assert.AreEqual(1, handler.Subscriptions.Count);
    }

    [TestMethod]
    public async Task SubscribeAsync_ReturnsExistingSubscriptionWithoutCreatingDuplicates()
    {
        var handler = new FakeMaxioHandler();
        handler.Customers.Add(new FakeMaxioCustomer
        {
            Id = 1,
            Reference = DemoUser.Reference,
            Email = DemoUser.Email,
            FirstName = DemoUser.FirstName,
            LastName = DemoUser.LastName
        });
        handler.Subscriptions.Add(new FakeMaxioSubscription
        {
            Id = 42,
            CustomerReference = DemoUser.Reference,
            ProductHandle = ProHandle
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(DemoUser, ProHandle, default);

        Assert.IsFalse(result.Created);
        Assert.IsNotNull(result.Subscription);
        Assert.AreEqual(42, result.Subscription!.Id);
        Assert.AreEqual(ProHandle, result.Subscription.PlanHandle);
        Assert.AreEqual(0, handler.CustomerPosts);
        Assert.AreEqual(0, handler.SubscriptionPosts);
    }

    [TestMethod]
    public async Task SubscribeAsync_AllowsResubscribeAfterSubscriptionIsCanceled()
    {
        var handler = new FakeMaxioHandler();
        handler.Customers.Add(new FakeMaxioCustomer
        {
            Id = 1,
            Reference = DemoUser.Reference,
            Email = DemoUser.Email,
            FirstName = DemoUser.FirstName,
            LastName = DemoUser.LastName
        });
        handler.Subscriptions.Add(new FakeMaxioSubscription
        {
            Id = 42,
            CustomerReference = DemoUser.Reference,
            ProductHandle = ProHandle,
            State = "canceled"
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(DemoUser, ProHandle, default);

        Assert.IsTrue(result.Created);
        Assert.AreEqual(1, handler.SubscriptionPosts);
    }

    [TestMethod]
    public async Task SubscribeAsync_RejectsUnknownPlan()
    {
        var handler = new FakeMaxioHandler();
        var service = CreateService(handler);

        var exception = await Assert.ThrowsExceptionAsync<MaxioApiException>(
            () => service.SubscribeAsync(DemoUser, "no-such-plan", default));

        Assert.AreEqual(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.AreEqual(0, handler.CustomerPosts);
        Assert.AreEqual(0, handler.SubscriptionPosts);
    }

    [TestMethod]
    public async Task SubscribeAsync_ConcurrentDoubleClickCreatesSingleSubscription()
    {
        var handler = new FakeMaxioHandler();
        var service = CreateService(handler);

        var results = await Task.WhenAll(
            service.SubscribeAsync(DemoUser, ProHandle, default),
            service.SubscribeAsync(DemoUser, ProHandle, default));

        Assert.AreEqual(1, results.Count(r => r.Created));
        Assert.AreEqual(1, results.Count(r => !r.Created));
        Assert.AreEqual(1, handler.CustomerPosts);
        Assert.AreEqual(1, handler.SubscriptionPosts);
        Assert.AreEqual(1, handler.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ListSubscriptionsAsync_ReturnsEmptyWhenNoCustomerExists()
    {
        var handler = new FakeMaxioHandler();
        var service = CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync("unknown-user", default);

        Assert.AreEqual(0, subscriptions.Count);
    }

    private static MaxioSubscriptionService CreateService(FakeMaxioHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(handler),
            new MaxioAdvancedBillingClientOptions
            {
                Environment = ServerEnvironment.Us
            });
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-8",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioSubscriptionService(client, settings, new SubscriberLock(), NullLogger<MaxioSubscriptionService>.Instance);
    }
}
