using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints identify the shopper from the bearer token alone, so an unauthenticated
/// caller must be refused before any billing work happens. These tests never reach the billing provider.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthorizationTest
{
    [TestMethod]
    public async Task ListPlansRequiresAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsRequiresAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresAToken()
    {
        var content = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");

        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsAMissingPlanHandleBeforeContactingBilling()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiTokenHelper.GetNormalUserToken()}");
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
