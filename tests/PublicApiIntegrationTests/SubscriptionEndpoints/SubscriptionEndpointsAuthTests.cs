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
public class SubscriptionEndpointsAuthTests
{
    [TestMethod]
    public async Task ListPlansReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionReturnsUnauthorizedWithoutToken()
    {
        var json = JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "eshop-pro" });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", content);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlansAcceptsBearerToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreNotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreNotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
