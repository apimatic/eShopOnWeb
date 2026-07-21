using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class ListCustomerSubscriptionsAsyncTests
{
    [Fact]
    public async Task ReturnsEmptyListWhenNoCustomerExistsYet()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Empty(HttpStatusCode.NotFound),
            SequentialStubHandler.Json(HttpStatusCode.OK, "[]"));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var subscriptions = await client.ListCustomerSubscriptionsAsync("nobody@example.com");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task MapsEachSubscriptionsStateAndPriceCorrectly()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "customer": { "id": 42, "email": "shopper@example.com", "reference": "shopper@example.com" } }"""),
            SequentialStubHandler.Json(HttpStatusCode.OK, """
            [
              { "subscription": { "id": 1001, "state": "active", "current_period_ends_at": "2026-08-01T00:00:00Z", "cancel_at_end_of_period": false,
                  "product": { "id": 7127070, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 } } },
              { "subscription": { "id": 1002, "state": "on_hold", "cancel_at_end_of_period": false,
                  "product": { "id": 7127071, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900 } } }
            ]
            """));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var subscriptions = await client.ListCustomerSubscriptionsAsync("shopper@example.com");

        Assert.Equal(2, subscriptions.Count);

        var active = Assert.Single(subscriptions, s => s.Id == 1001);
        Assert.Equal(SubscriptionStatus.Active, active.Status);
        Assert.Equal(299.00m, active.Price);
        Assert.Equal("shopper@example.com", active.CustomerReference);
        Assert.NotNull(active.CurrentPeriodEndsAt);

        // Maxio's wire value for a held subscription is "on_hold"; both that and "paused" must
        // fold into the same provider-agnostic Paused signal.
        var held = Assert.Single(subscriptions, s => s.Id == 1002);
        Assert.Equal(SubscriptionStatus.Paused, held.Status);
    }
}
