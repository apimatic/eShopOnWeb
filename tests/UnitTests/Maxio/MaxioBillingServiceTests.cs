using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioBillingServiceTests
{
    private static MaxioSettings Settings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe",
        PaymentCollectionMethod = "remittance",
    };

    private static MaxioBillingService CreateService(FakeMaxioApiClient client) =>
        new(client, Settings(), NullLogger<MaxioBillingService>.Instance);

    private static SubscriberIdentity Subscriber(string reference) =>
        new(reference, $"{reference}@example.com", "Test", "User");

    private static FakeMaxioApiClient ClientWithPlans()
    {
        var client = new FakeMaxioApiClient();
        client.Products.Add(new MaxioProduct { Id = 1, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" });
        client.Products.Add(new MaxioProduct { Id = 2, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" });
        client.Products.Add(new MaxioProduct { Id = 3, Handle = "archived", Name = "Old", PriceInCents = 100, ArchivedAt = "2020-01-01T00:00:00Z" });
        return client;
    }

    [Fact]
    public async Task GetPlansAsync_FiltersArchived_AndMapsAndSortsByPrice()
    {
        var service = CreateService(ClientWithPlans());

        var plans = (await service.GetPlansAsync()).ToList();

        Assert.Equal(2, plans.Count); // archived excluded
        Assert.Equal("basic-plan", plans[0].Handle); // sorted by price ascending
        Assert.Equal("eshop-pro", plans[1].Handle);
        Assert.Equal("$299.00", plans[1].FormattedPrice);
        Assert.Equal("month", plans[1].IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesSubscription_WhenNoneExists()
    {
        var client = ClientWithPlans();
        var service = CreateService(client);

        var result = await service.SubscribeAsync(Subscriber("new-user-1"), "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(29900, result.ProductPriceInCents);
        Assert.Equal(1, client.CreateCustomerCalls);
        Assert.Equal(1, client.CreateSubscriptionCalls);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExisting_WhenActiveSubscriptionExists()
    {
        var client = ClientWithPlans();
        var service = CreateService(client);
        var subscriber = Subscriber("existing-user-1");

        var first = await service.SubscribeAsync(subscriber, "eshop-pro");
        var second = await service.SubscribeAsync(subscriber, "eshop-pro");

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, client.CreateSubscriptionCalls); // no duplicate
        Assert.Equal(1, client.CreateCustomerCalls);     // customer reused
    }

    [Fact]
    public async Task SubscribeAsync_CreatesNew_WhenOnlyTerminalSubscriptionExists()
    {
        var client = ClientWithPlans();
        var customer = new MaxioCustomer { Id = 500, Reference = "churned@example.com", Email = "churned@example.com" };
        client.SeedSubscription(customer, "eshop-pro", state: "canceled");
        var service = CreateService(client);

        var result = await service.SubscribeAsync(
            new SubscriberIdentity("churned@example.com", "churned@example.com", "C", "U"), "eshop-pro");

        Assert.False(result.AlreadyExisted);
        Assert.Equal("active", result.State);
        Assert.Equal(1, client.CreateSubscriptionCalls);
        Assert.Equal(0, client.CreateCustomerCalls); // existing customer reused
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_ThrowsBillingException()
    {
        var service = CreateService(ClientWithPlans());

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Subscriber("user-x"), "nope"));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public async Task SubscribeAsync_BlankPlan_ThrowsBillingException()
    {
        var service = CreateService(ClientWithPlans());

        await Assert.ThrowsAsync<BillingException>(
            () => service.SubscribeAsync(Subscriber("user-y"), "   "));
    }

    [Fact]
    public async Task SubscribeAsync_ConcurrentCalls_CreateExactlyOneSubscription()
    {
        var client = ClientWithPlans();
        client.CreateDelay = TimeSpan.FromMilliseconds(50); // widen the race window
        var service = CreateService(client);
        var subscriber = Subscriber("race-user-1");

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => service.SubscribeAsync(subscriber, "eshop-pro"))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, client.CreateSubscriptionCalls);
        Assert.Equal(1, client.CreateCustomerCalls);
        Assert.Single(results.Where(r => !r.AlreadyExisted));
        Assert.Equal(7, results.Count(r => r.AlreadyExisted));
        Assert.Single(results.Select(r => r.Id).Distinct());
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmpty_WhenNoCustomer()
    {
        var service = CreateService(ClientWithPlans());

        var subs = await service.GetSubscriptionsAsync(Subscriber("nobody-here"));

        Assert.Empty(subs);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsCustomerSubscriptions()
    {
        var client = ClientWithPlans();
        var service = CreateService(client);
        var subscriber = Subscriber("lister-1");
        await service.SubscribeAsync(subscriber, "eshop-pro");

        var subs = (await service.GetSubscriptionsAsync(subscriber)).ToList();

        Assert.Single(subs);
        Assert.Equal("eshop-pro", subs[0].PlanHandle);
        Assert.Equal(subscriber.Reference, subs[0].CustomerReference);
    }
}
