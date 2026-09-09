using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamiliesJson =
        """[{"product_family":{"id":1,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]""";

    private const string ProductsJson =
        """[{"product":{"id":10,"handle":"eshop-pro","name":"Pro Plan","description":"The pro plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}},{"product":{"id":11,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false}}]""";

    private static readonly SubscriptionUserInfo TestUser = new("user-1", "demouser@microsoft.com", "Demo", "User");

    private static (MaxioSubscriptionBillingService Service, StubMaxioHandler Handler) Build(StubMaxioHandler handler)
    {
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe"
        });
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var service = new MaxioSubscriptionBillingService(client, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task SubscribeTwice_CreatesCustomerAndSubscriptionOnce_SecondCallReturnsExisting()
    {
        var handler = new StubMaxioHandler
        {
            FamiliesJson = FamiliesJson,
            ProductsJson = ProductsJson,
            CustomerExists = false
        };
        var (service, _) = Build(handler);

        var first = await service.SubscribeAsync(TestUser, "eshop-pro");
        var second = await service.SubscribeAsync(TestUser, "eshop-pro");

        Assert.False(first.AlreadySubscribed);
        Assert.True(second.AlreadySubscribed);
        Assert.Equal(first.Subscription.SubscriptionId, second.Subscription.SubscriptionId);
        Assert.Equal(1, handler.CustomerCreateCount);
        Assert.Equal(1, handler.SubscriptionCreateCount);
        Assert.Equal("Pro Plan", first.Subscription.PlanName);
        Assert.Equal(299.00m, first.Subscription.Price);
        Assert.Equal("active", first.Subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero), first.Subscription.NextBillingDate);
    }

    [Fact]
    public async Task SubscribeTwice_SecondCallSkipsCreateCalls()
    {
        var handler = new StubMaxioHandler
        {
            FamiliesJson = FamiliesJson,
            ProductsJson = ProductsJson,
            CustomerExists = false
        };
        var (service, _) = Build(handler);

        await service.SubscribeAsync(TestUser, "eshop-pro");
        var postsAfterFirst = handler.CountRequests(HttpMethod.Post, "/subscriptions");
        await service.SubscribeAsync(TestUser, "eshop-pro");

        Assert.Equal(1, postsAfterFirst);
        Assert.Equal(1, handler.CustomerCreateCount);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_ThrowsNotFound()
    {
        var handler = new StubMaxioHandler
        {
            FamiliesJson = FamiliesJson,
            ProductsJson = ProductsJson
        };
        var (service, _) = Build(handler);

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => service.SubscribeAsync(TestUser, "no-such-plan"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(0, handler.SubscriptionCreateCount);
        // No billing profile is provisioned for a bogus plan.
        Assert.Equal(0, handler.CustomerCreateCount);
    }

    [Fact]
    public async Task ListSubscriptionsForUser_NoBillingProfile_ReturnsEmptyWithoutCreating()
    {
        var handler = new StubMaxioHandler
        {
            FamiliesJson = FamiliesJson,
            ProductsJson = ProductsJson,
            CustomerExists = false
        };
        var (service, _) = Build(handler);

        var subscriptions = await service.ListSubscriptionsForUserAsync(TestUser);

        Assert.Empty(subscriptions);
        Assert.Equal(0, handler.CustomerCreateCount);
        Assert.Equal(0, handler.SubscriptionCreateCount);
    }

    [Fact]
    public async Task ListSubscriptionsForUser_WithSubscriptions_MapsPlanPriceStateAndNextBilling()
    {
        var handler = new StubMaxioHandler
        {
            FamiliesJson = FamiliesJson,
            ProductsJson = ProductsJson,
            CustomerExists = true,
            CustomerSubscriptionsJson =
                """[{"subscription":{"id":777,"state":"active","product_id":10,"product_price_in_cents":29900,"next_assessment_at":"2026-10-09T00:00:00Z","current_period_ends_at":"2026-10-09T00:00:00Z","product":{"id":10,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}]"""
        };
        var (service, _) = Build(handler);

        var subscriptions = await service.ListSubscriptionsForUserAsync(TestUser);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(777, subscription.SubscriptionId);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal("active", subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);
    }

    [Fact]
    public async Task ListPlans_MapsFamilyProducts()
    {
        var handler = new StubMaxioHandler
        {
            FamiliesJson = FamiliesJson,
            ProductsJson = ProductsJson
        };
        var (service, _) = Build(handler);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(pro.RequiresPaymentMethod);
    }
}
