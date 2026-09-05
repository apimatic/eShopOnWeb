using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class ListSubscriptionsAsyncTests
{
    [Fact]
    public async Task ReturnsAnEmptyList_WhenNoBillingCustomerExistsYet()
    {
        var handler = new FakeMaxioHandler()
            .When(HttpMethod.Get, "customers/lookup.json", _ => FakeMaxioHandler.NotFound());

        var service = MaxioTestFactory.CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync("never-subscribed@example.com");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ReturnsTheCustomersSubscriptions_MappedFromMaxio()
    {
        var handler = new FakeMaxioHandler()
            .When(HttpMethod.Get, "customers/lookup.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK,
                """{"customer":{"id":501,"reference":"shopper@example.com","first_name":"Shopper","last_name":"Customer","email":"shopper@example.com"}}"""))
            .When(HttpMethod.Get, "customers/501/subscriptions.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK,
                """
                [{"subscription":{"id":9001,"state":"active","reference":"eshoponweb:shopper@example.com:eshop-pro",
                  "current_period_ends_at":"2026-10-05T00:00:00Z","next_assessment_at":"2026-10-05T00:00:00Z","created_at":"2026-09-05T00:00:00Z",
                  "product":{"id":10,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}]
                """));

        var service = MaxioTestFactory.CreateService(handler);

        var subscriptions = await service.ListSubscriptionsAsync("shopper@example.com");

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(9001, subscription.SubscriptionId);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(29900, subscription.PriceInCents);
    }
}
