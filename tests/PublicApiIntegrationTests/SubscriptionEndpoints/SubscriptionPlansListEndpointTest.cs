using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionPlansListEndpointTest
{
    [TestMethod]
    public async Task ReturnsSeededPlans()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.AreEqual(2, model!.Plans.Count);
        Assert.IsTrue(model.Plans.Exists(p => p.PlanHandle == "eshop-pro" && p.Price == 299.00m));
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
