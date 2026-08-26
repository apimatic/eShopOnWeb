using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class ListSubscriptionPlansEndpointTest
{
    [TestMethod]
    public async Task ReturnsPlansFromBillingProvider()
    {
        var handler = MaxioTestServer.ForPlans();
        using var factory = MaxioTestServer.CreateFactory(handler);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(2, model.Plans.Count);

        var pro = model.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.AreEqual("Pro Plan", pro.Name);
        Assert.AreEqual(299.00m, pro.Price);
        Assert.AreEqual(29900, pro.PriceInCents);
        Assert.AreEqual(1, pro.Interval);
        Assert.AreEqual("month", pro.IntervalUnit);

        // The family handle was resolved to its numeric id first, then products listed for that id.
        Assert.AreEqual(1, handler.CountRequests(HttpMethod.Get, "/product_families.json"));
        Assert.AreEqual(1, handler.CountRequests(HttpMethod.Get, "/product_families/3023074/products.json"));
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        using var factory = MaxioTestServer.CreateFactory(MaxioTestServer.ForPlans());
        var client = factory.CreateClient();

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
