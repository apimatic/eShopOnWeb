using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class ListSubscriptionPlansEndpointTest
{
    [TestMethod]
    public async Task ReturnsPlansForAuthenticatedUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Plans.Count >= 2);
        Assert.IsTrue(model.Plans.Exists(p => p.Handle == "eshop-pro"));
        Assert.IsTrue(model.Plans.Exists(p => p.Handle == "basic-plan"));
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
