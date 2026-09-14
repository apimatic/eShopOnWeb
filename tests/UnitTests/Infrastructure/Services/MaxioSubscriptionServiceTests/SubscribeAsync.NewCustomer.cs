using System.Net;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.MaxioSubscriptionServiceTests;

public class SubscribeAsyncNewCustomer
{
    [Fact]
    public async Task CreatesCustomerThenSubscription()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.NotFound, """{ "errors": ["Customer not found"] }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.Created, """{ "customer": { "id": 555, "reference": "user-1" } }"""),
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, "[]"),
            MaxioTestSupport.ReadSiteResponder(),
            _ => MaxioTestSupport.Json(HttpStatusCode.Created, """
                {
                    "subscription": {
                        "id": 999,
                        "state": "active",
                        "next_assessment_at": "2026-10-05T00:00:00Z",
                        "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }
                    }
                }
                """));
        var service = MaxioTestSupport.CreateService(handler);

        var subscription = await service.SubscribeAsync("user-1", "jane.doe@example.com", "eshop-pro");

        Assert.Equal(999, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal("active", subscription.State);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), subscription.NextBillingDate);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task DerivesCustomerNameFromEmailWhenCreatingCustomer()
    {
        var handler = new MaxioSequencedHandler(
            _ => MaxioTestSupport.Json(HttpStatusCode.NotFound, """{ "errors": [] }"""),
            request =>
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Contains("\"first_name\":\"Jane\"", body);
                Assert.Contains("\"last_name\":\"Doe\"", body);
                Assert.Contains("\"email\":\"jane.doe@example.com\"", body);
                Assert.Contains("\"reference\":\"user-1\"", body);
                return MaxioTestSupport.Json(HttpStatusCode.Created, """{ "customer": { "id": 555 } }""");
            },
            _ => MaxioTestSupport.Json(HttpStatusCode.OK, "[]"),
            MaxioTestSupport.ReadSiteResponder(),
            _ => MaxioTestSupport.Json(HttpStatusCode.Created, """
                { "subscription": { "id": 999, "state": "active", "product": { "handle": "eshop-pro" } } }
                """));
        var service = MaxioTestSupport.CreateService(handler);

        await service.SubscribeAsync("user-1", "jane.doe@example.com", "eshop-pro");
    }
}
