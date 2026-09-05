using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsAuthTest
{
    [TestMethod]
    public async Task ListPlans_WithoutToken_ReturnsUnauthorized()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_WithoutToken_ReturnsUnauthorized()
    {
        var content = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptions_WithoutToken_ReturnsUnauthorized()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_WithToken_MissingPlanHandle_ReturnsBadRequest()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
