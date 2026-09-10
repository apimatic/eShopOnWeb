using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionServiceTests
{
    private static readonly SubscriberIdentity Subscriber =
        SubscriberIdentity.FromUserName("demouser@microsoft.com");

    private static (MaxioSubscriptionService Service, FakeMaxioHandler Handler) CreateService()
    {
        var handler = new FakeMaxioHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example-site.chargify.com/") };
        var apiClient = new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "example-site",
            ProductFamilyHandle = "eshop-subscribe",
        });

        var service = new MaxioSubscriptionService(apiClient, settings, NullLogger<MaxioSubscriptionService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task GetPlansAsync_ReturnsPlansFromConfiguredFamily()
    {
        var (service, _) = CreateService();

        var plans = await service.GetPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.PriceInCents == 29900);
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.PriceInCents == 2900);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription_WhenNoneExist()
    {
        var (service, handler) = CreateService();

        var subscription = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(29900, subscription.CurrentPriceInCents);
        Assert.NotNull(subscription.NextBillingAt);
        Assert.Equal(1, handler.CreateCustomerCount);
        Assert.Equal(1, handler.CreateSubscriptionCount);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotent_ForRepeatedCalls()
    {
        var (service, handler) = CreateService();

        var first = await service.SubscribeAsync(Subscriber, "eshop-pro");
        var second = await service.SubscribeAsync(Subscriber, "eshop-pro");

        Assert.Equal(first.Id, second.Id);
        // The customer is created once and the subscription is created once, no matter how many times we call.
        Assert.Equal(1, handler.CreateCustomerCount);
        Assert.Equal(1, handler.CreateSubscriptionCount);
    }

    [Fact]
    public async Task SubscribeAsync_DefaultsToHighestPricedPlan_WhenHandleOmitted()
    {
        var (service, _) = CreateService();

        var subscription = await service.SubscribeAsync(Subscriber, planHandle: null);

        Assert.Equal("eshop-pro", subscription.PlanHandle);
    }

    [Fact]
    public async Task SubscribeAsync_Throws_ForUnknownPlan()
    {
        var (service, _) = CreateService();

        var ex = await Assert.ThrowsAsync<MaxioException>(() => service.SubscribeAsync(Subscriber, "no-such-plan"));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsEmpty_WhenNoCustomerExists()
    {
        var (service, _) = CreateService();

        var subscriptions = await service.GetSubscriptionsAsync(Subscriber);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ReturnsSubscription_AfterSubscribing()
    {
        var (service, _) = CreateService();
        await service.SubscribeAsync(Subscriber, "eshop-pro");

        var subscriptions = await service.GetSubscriptionsAsync(Subscriber);

        Assert.Single(subscriptions);
        Assert.Equal("eshop-pro", subscriptions.Single().PlanHandle);
    }
}
