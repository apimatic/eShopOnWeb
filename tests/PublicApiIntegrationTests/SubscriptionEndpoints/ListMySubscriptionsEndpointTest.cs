using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class ListMySubscriptionsEndpointTest
{
    [TestMethod]
    public async Task ReturnsSubscriptionsForAuthenticatedUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>();
        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Subscriptions.Count >= 1);
        Assert.IsTrue(model.Subscriptions.Exists(s => s.PlanHandle == "eshop-pro"));
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
