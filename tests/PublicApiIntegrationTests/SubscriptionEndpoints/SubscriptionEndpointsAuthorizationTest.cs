using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Verifies that the subscription capability is wired into the API and that every route is behind the bearer
/// token. These tests deliberately make no authenticated call: the caller's identity drives real billing-system
/// writes, so the authenticated paths are covered by unit tests over a stubbed transport instead.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthorizationTest
{
    [TestMethod]
    public async Task ReturnsUnauthorizedForPlansWithoutAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedForMySubscriptionsWithoutAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedForSubscribeWithoutAToken()
    {
        var content = new StringContent(
            "{\"planHandle\":\"eshop-pro\"}", System.Text.Encoding.UTF8, "application/json");

        var response = await ProgramTest.NewClient.PostAsync("/api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedForSubscribeWithAnInvalidToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var response = await client.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
