using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio.MaxioSubscriptionServiceTests;

public class ListPlansAsync
{
    [Fact]
    public async Task ReturnsPlansMappedFromTheProductFamily()
    {
        const string json = """
        [
          {
            "product": {
              "id": 7126957,
              "name": "Pro Plan",
              "handle": "eshop-pro",
              "price_in_cents": 29900,
              "interval": 1,
              "interval_unit": "month",
              "require_credit_card": false
            }
          },
          {
            "product": {
              "id": 7126958,
              "name": "Basic Plan",
              "handle": "basic-plan",
              "price_in_cents": 2900,
              "interval": 1,
              "interval_unit": "month",
              "require_credit_card": false
            }
          }
        ]
        """;

        var (service, handler) = MaxioTestClientFactory.Create(_ => MaxioTestClientFactory.JsonResponse(HttpStatusCode.OK, json));

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal("Pro Plan", plans[0].Name);
        Assert.Equal(29900, plans[0].PriceInCents);
        Assert.Equal("month", plans[0].IntervalUnit);
        Assert.Equal("basic-plan", plans[1].Handle);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(System.Net.Http.HttpMethod.Get, request.Method);
        Assert.Contains("eshop-subscribe", request.RequestUri!.ToString());
    }
}
