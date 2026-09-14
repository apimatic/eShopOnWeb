using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services.MaxioSubscriptionServiceTests;

public class ListPlansAsync
{
    [Fact]
    public async Task ReturnsPlansForTheConfiguredProductFamily()
    {
        var (service, handler) = MaxioSubscriptionServiceTestFactory.Create(
            "eshop-subscribe",
            (HttpStatusCode.OK, """
                [{"product_family": {"id": 3023074, "handle": "eshop-subscribe", "name": "eShop Subscribe"}}]
                """),
            (HttpStatusCode.OK, """
                [
                  {"product": {"id": 7126957, "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "taxable": false, "require_credit_card": false, "request_credit_card": false}},
                  {"product": {"id": 7126958, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "taxable": false, "require_credit_card": false, "request_credit_card": false}}
                ]
                """));

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal("Pro Plan", plans[0].Name);
        Assert.Equal(29900, plans[0].PriceInCents);
        Assert.Equal("month", plans[0].IntervalUnit);
        Assert.False(plans[0].RequiresPaymentMethod);
        Assert.Equal(2, handler.Requests.Count);
    }
}
