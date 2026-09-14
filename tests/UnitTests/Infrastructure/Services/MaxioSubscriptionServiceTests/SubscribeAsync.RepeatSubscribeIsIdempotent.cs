using System.Net;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.MaxioSubscriptionServiceTests;

public class SubscribeAsyncRepeatSubscribeIsIdempotent
{
    /// <summary>
    /// A double-click on Subscribe (or subscribing again while already enrolled) must not create a
    /// second subscription - the existing, non-terminal enrollment for that plan is returned instead.
    /// </summary>
    [Fact]
    public async Task ReturnsTheExistingSubscriptionWithoutCreatingASecondOne()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "user-1" } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """
                [
                    {
                        "subscription": {
                            "id": 999,
                            "state": "active",
                            "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
                        }
                    }
                ]
                """));
        var service = MaxioTestSupport.CreateService(handler);

        var subscription = await service.SubscribeAsync("user-1", "jane.doe@example.com", "eshop-pro");

        Assert.Equal(999, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ACanceledSubscriptionForThePlanDoesNotBlockResubscribing()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "reference": "user-1" } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, """
                [
                    {
                        "subscription": {
                            "id": 111,
                            "state": "canceled",
                            "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
                        }
                    }
                ]
                """),
            MaxioTestSupport.ReadSiteResponder(),
            _ => MaxioTestSupport.Json(HttpStatusCode.Created, """
                { "subscription": { "id": 222, "state": "active", "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 } } }
                """));
        var service = MaxioTestSupport.CreateService(handler);

        var subscription = await service.SubscribeAsync("user-1", "jane.doe@example.com", "eshop-pro");

        Assert.Equal(222, subscription.Id);
        Assert.Equal(4, handler.Requests.Count);
    }
}
