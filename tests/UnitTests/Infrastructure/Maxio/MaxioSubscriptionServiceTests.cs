using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private static MaxioProduct Product(string handle, string name, long priceInCents) => new()
    {
        Id = handle.GetHashCode(),
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month"
    };

    private static FakeMaxioApiClient BuildClient() => new(new[]
    {
        Product("basic-plan", "Basic Plan", 2900),
        Product("eshop-pro", "Pro Plan", 29900)
    });

    private static MaxioSubscriptionService BuildService(FakeMaxioApiClient client)
    {
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioSubscriptionService(client, settings, new NullAppLogger<MaxioSubscriptionService>());
    }

    private static SubscriberIdentity Subscriber(string reference) =>
        new(reference, $"{reference}@example.com", reference, "Test");

    [Fact]
    public async Task GetAvailablePlans_ReturnsFamilyProducts()
    {
        var service = BuildService(BuildClient());

        var plans = await service.GetAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.PriceInCents == 29900);
    }

    [Fact]
    public async Task Subscribe_FirstTime_CreatesCustomerAndSubscription()
    {
        var client = BuildClient();
        var service = BuildService(client);

        var result = await service.SubscribeAsync(Subscriber("user-new"), "eshop-pro");

        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(1, client.CustomerCreateCount);
        Assert.Equal(1, client.SubscriptionCreateCount);
    }

    [Fact]
    public async Task Subscribe_WhenAlreadySubscribedToPlan_ReturnsExistingAndDoesNotDuplicate()
    {
        var client = BuildClient();
        var service = BuildService(client);
        var subscriber = Subscriber("user-repeat");

        var first = await service.SubscribeAsync(subscriber, "eshop-pro");
        var second = await service.SubscribeAsync(subscriber, "eshop-pro");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, client.CustomerCreateCount);
        Assert.Equal(1, client.SubscriptionCreateCount);
    }

    [Fact]
    public async Task Subscribe_ConcurrentDoubleClick_CreatesSingleCustomerAndSubscription()
    {
        var client = BuildClient();
        client.ArtificialLatency = TimeSpan.FromMilliseconds(25);
        var service = BuildService(client);
        var subscriber = Subscriber("user-doubleclick");

        var task1 = service.SubscribeAsync(subscriber, "basic-plan");
        var task2 = service.SubscribeAsync(subscriber, "basic-plan");
        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(1, client.CustomerCreateCount);
        Assert.Equal(1, client.SubscriptionCreateCount);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_ThrowsPlanNotFound()
    {
        var service = BuildService(BuildClient());

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => service.SubscribeAsync(Subscriber("user-x"), "no-such-plan"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Subscribe_MissingPlanHandle_ThrowsValidation(string planHandle)
    {
        var service = BuildService(BuildClient());

        await Assert.ThrowsAsync<SubscriptionValidationException>(
            () => service.SubscribeAsync(Subscriber("user-x"), planHandle));
    }

    [Fact]
    public async Task GetSubscriptions_NoCustomer_ReturnsEmpty()
    {
        var service = BuildService(BuildClient());

        var subscriptions = await service.GetSubscriptionsAsync(Subscriber("user-none"));

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetSubscriptions_AfterSubscribe_ReturnsSubscription()
    {
        var client = BuildClient();
        var service = BuildService(client);
        var subscriber = Subscriber("user-list");

        await service.SubscribeAsync(subscriber, "basic-plan");
        var subscriptions = await service.GetSubscriptionsAsync(subscriber);

        Assert.Single(subscriptions);
        Assert.Equal("basic-plan", subscriptions[0].PlanHandle);
    }

    private sealed class NullAppLogger<T> : IAppLogger<T>
    {
        public void LogInformation(string message, params object[] args) { }
        public void LogWarning(string message, params object[] args) { }
    }
}
