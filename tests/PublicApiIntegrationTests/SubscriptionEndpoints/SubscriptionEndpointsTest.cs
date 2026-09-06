using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Contract checks for the subscription endpoints that hold regardless of how the billing provider
/// is configured. The paths that talk to Maxio are covered by the unit tests, which script the
/// transport instead of calling the live sandbox.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task ListPlansReturnsUnauthorizedWithoutAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsReturnsUnauthorizedWithoutAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeReturnsUnauthorizedWithoutAToken()
    {
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", PlanHandleJson("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeReturnsUnauthorizedGivenAnUnsignedToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.real.token");

        var response = await client.PostAsync("api/subscriptions", PlanHandleJson("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsARequestWithoutAPlanHandleBeforeCallingTheProvider()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", Json("{}"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "planHandle");
    }

    private static StringContent PlanHandleJson(string planHandle) => Json($"{{\"planHandle\":\"{planHandle}\"}}");

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
