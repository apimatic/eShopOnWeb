using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class ListPlansAsyncTests
{
    [Fact]
    public async Task ReturnsOnlyActiveProductsBelongingToTheConfiguredFamily()
    {
        var handler = new FakeMaxioHandler()
            .When(HttpMethod.Get, "product_families/handle:eshop-subscribe.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK,
                """{"product_family":{"id":1,"name":"eShop Subscribe","handle":"eshop-subscribe"}}"""))
            .When(HttpMethod.Get, "products.json", _ => FakeMaxioHandler.Json(HttpStatusCode.OK,
                """
                [
                  {"product":{"id":10,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,"product_family":{"id":1,"handle":"eshop-subscribe"}}},
                  {"product":{"id":11,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month","archived_at":null,"product_family":{"id":1,"handle":"eshop-subscribe"}}},
                  {"product":{"id":99,"name":"Unrelated Plan","handle":"unrelated","price_in_cents":500,"interval":1,"interval_unit":"month","archived_at":null,"product_family":{"id":2,"handle":"other-family"}}},
                  {"product":{"id":100,"name":"Archived Plan","handle":"archived","price_in_cents":100,"interval":1,"interval_unit":"month","archived_at":"2020-01-01T00:00:00Z","product_family":{"id":1,"handle":"eshop-subscribe"}}}
                ]
                """));

        var service = MaxioTestFactory.CreateService(handler);

        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, p => p.Handle == "eshop-pro" && p.PriceInCents == 29900);
        Assert.Contains(plans, p => p.Handle == "basic-plan" && p.PriceInCents == 2900);
        Assert.DoesNotContain(plans, p => p.Handle is "unrelated" or "archived");
    }
}
