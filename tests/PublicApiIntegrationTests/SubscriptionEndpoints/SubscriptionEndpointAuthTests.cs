using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointAuthTests
{
    [TestMethod]
    public async Task ListPlans_ReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptions_ReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_ReturnsUnauthorizedWithoutToken()
    {
        var json = new StringContent(JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "eshop-pro" }), Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", json);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlans_AcceptsNormalUserToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreNotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreNotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
