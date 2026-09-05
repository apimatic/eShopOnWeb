using System.Net;
using System.Threading.Tasks;
using Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.MaxioSubscriptionServiceTests;

public class ListSubscriptionsAsync
{
    [Fact]
    public async Task ReturnsEmpty_WhenTheUserHasNeverSubscribed()
    {
        var (service, handler) = MaxioTestClientFactory.Create(
            MaxioTestClientFactory.Sequenced(
                MaxioTestClientFactory.Respond(HttpStatusCode.NotFound, "\"not found\""))); // ReadCustomerByReference miss

        var subscriptions = await service.ListSubscriptionsAsync("nobody@microsoft.com");

        Assert.Empty(subscriptions);
        // No subscription list call is made for a customer that doesn't exist.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ReturnsMappedSubscriptions_WhenTheCustomerExists()
    {
        const string customerJson = """{ "customer": { "id": 555, "reference": "demouser@microsoft.com" } }""";
        const string subscriptionsJson = """
        [
          {
            "subscription": {
              "state": "active",
              "next_assessment_at": "2026-10-05T00:00:00Z",
              "product_price_in_cents": 29900,
              "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro" },
              "customer": { "id": 555 }
            }
          }
        ]
        """;

        var (service, _) = MaxioTestClientFactory.Create(MaxioTestClientFactory.Sequenced(
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, customerJson),
            MaxioTestClientFactory.Respond(HttpStatusCode.OK, subscriptionsJson)));

        var subscriptions = await service.ListSubscriptionsAsync("demouser@microsoft.com");

        var subscription = Assert.Single(subscriptions);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("active", subscription.State);
        Assert.Equal(29900, subscription.PriceInCents);
    }
}
