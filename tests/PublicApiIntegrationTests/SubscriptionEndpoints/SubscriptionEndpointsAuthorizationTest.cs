using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints identify the shopper from the bearer token, so an anonymous
/// caller must never reach them. These tests stop short of calling Maxio: they only assert
/// that the routes exist and are gated.
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
    public async Task SubscribeRejectsARequestWithNoPlanHandleBeforeTouchingTheBillingSystem()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
