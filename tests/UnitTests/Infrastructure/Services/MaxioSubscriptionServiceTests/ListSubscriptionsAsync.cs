using System.Net;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.MaxioSubscriptionServiceTests;

public class ListSubscriptionsAsync
{
    [Fact]
    public async Task ReturnsAnEmptyListWhenTheUserHasNeverSubscribed()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.NotFound, """{ "errors": [] }"""));
        var service = MaxioTestSupport.CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync("user-with-no-customer");

        Assert.Empty(subscriptions);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ReturnsTheCustomersSubscriptions()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "user-1" } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """
                [
                    {
                        "subscription": {
                            "id": 999,
                            "state": "active",
                            "next_assessment_at": "2026-10-05T00:00:00Z",
                            "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
                        }
                    }
                ]
                """));
        var service = MaxioTestSupport.CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync("user-1");

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(999, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("active", subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);
    }
}
