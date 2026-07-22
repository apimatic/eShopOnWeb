using System.Linq;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class SubscriptionCrudTests
{
    internal static string SubscriptionJson(int id, string state, string productHandle, long priceInCents,
        int customerId = 14714298) => $$"""
    {
      "subscription": {
        "id": {{id}},
        "state": "{{state}}",
        "current_period_ends_at": "2026-08-15T14:48:10-05:00",
        "cancel_at_end_of_period": false,
        "canceled_at": null,
        "customer": { "id": {{customerId}}, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" },
        "product": { "id": 7126957, "name": "Pro Plan", "handle": "{{productHandle}}", "price_in_cents": {{priceInCents}}, "interval": 1, "interval_unit": "month" }
      }
    }
    """;

    [Fact]
    public async Task CreateSubscription_PostsProductHandleAndCustomerId_MapsActiveSubscription()
    {
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.Created,
            SubscriptionJson(15236915, "active", "eshop-pro", 29900));

        var subscription = await client.CreateSubscriptionAsync(14714298, "eshop-pro");

        Assert.Equal(15236915, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.True(subscription.IsActive);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal("Pro Plan", subscription.ProductName);
        Assert.Equal(299.00m, subscription.ProductPrice);
        Assert.Equal(14714298, subscription.CustomerId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/subscriptions.json", request.PathAndQuery);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body);
        Assert.Contains("\"customer_id\":14714298", request.Body);
    }

    [Fact]
    public async Task ListCustomerSubscriptions_MapsAll()
    {
        var json = "[" + SubscriptionJson(1, "active", "eshop-pro", 29900)
            + "," + SubscriptionJson(2, "canceled", "basic-plan", 2900) + "]";
        var (client, _) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, json);

        var subs = (await client.ListCustomerSubscriptionsAsync(14714298)).ToList();

        Assert.Equal(2, subs.Count);
        Assert.Contains(subs, s => s.Id == 1 && s.IsActive);
        Assert.Contains(subs, s => s.Id == 2 && s.State == "canceled");
    }

    [Fact]
    public async Task ListCustomerSubscriptions_None_ReturnsEmpty()
    {
        var (client, _) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, "[]");

        Assert.Empty(await client.ListCustomerSubscriptionsAsync(999));
    }

    [Fact]
    public async Task GetSubscription_UnknownId_ThrowsTypedExceptionWithStatusCode()
    {
        var (client, _) = MaxioClientHarness.WithResponse(HttpStatusCode.NotFound, "");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.GetSubscriptionAsync(424242));
        Assert.Equal(404, ex.StatusCode);
    }
}
