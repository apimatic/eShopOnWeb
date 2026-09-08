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
public class MySubscriptionsEndpointTest
{
    [TestMethod]
    public async Task ReturnsEmptyListForUserWithNoCustomer()
    {
        MaxioTestSupport.RequireMaxioConfiguration();

        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken(MaxioTestSupport.NewIdentity()));

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var stringResponse = await response.Content.ReadAsStringAsync();
        var model = stringResponse.FromJson<ListSubscriptionsResponse>();

        Assert.IsNotNull(model);
        Assert.AreEqual(0, model!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedGivenNoToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
