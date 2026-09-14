using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services.MaxioSubscriptionServiceTests;

public class ListSubscriptionsForUserAsync
{
    [Fact]
    public async Task ReturnsMappedSubscriptionsForAnExistingCustomer()
    {
        var (service, handler) = MaxioSubscriptionServiceTestFactory.Create(
            "eshop-subscribe",
            (HttpStatusCode.OK, """{"customer": {"id": 55, "reference": "user-1"}}"""),
            (HttpStatusCode.OK, """
                [{"subscription": {"id": 900, "state": "active", "next_assessment_at": "2026-10-05T00:00:00Z",
                  "current_period_ends_at": "2026-11-05T00:00:00Z",
                  "product": {"handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900}}}]
                """));

        var subscriptions = await service.ListSubscriptionsForUserAsync("user-1");

        Assert.Single(subscriptions);
        Assert.Equal(900, subscriptions[0].MaxioSubscriptionId);
        Assert.Equal("eshop-pro", subscriptions[0].PlanHandle);
        Assert.Equal("active", subscriptions[0].State);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ReturnsEmptyWhenTheUserHasNeverSubscribed()
    {
        var (service, handler) = MaxioSubscriptionServiceTestFactory.Create(
            "eshop-subscribe",
            (HttpStatusCode.NotFound, ""));

        var subscriptions = await service.ListSubscriptionsForUserAsync("never-subscribed");

        Assert.Empty(subscriptions);
        Assert.Single(handler.Requests);
    }
}
