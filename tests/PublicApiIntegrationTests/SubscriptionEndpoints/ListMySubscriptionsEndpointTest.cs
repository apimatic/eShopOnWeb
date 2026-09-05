using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class ListMySubscriptionsEndpointTest
{
    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReflectsASubscriptionJustCreated()
    {
        var token = ApiTokenHelper.GetNormalUserToken();
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var subscribe = await client.PostAsync("api/subscriptions",
            new StringContent(JsonSerializer.Serialize(new { planHandle = "eshop-pro" }), Encoding.UTF8, "application/json"));
        subscribe.EnsureSuccessStatusCode();

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();

        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Subscriptions.Exists(s => s.PlanHandle == "eshop-pro"));
    }
}
