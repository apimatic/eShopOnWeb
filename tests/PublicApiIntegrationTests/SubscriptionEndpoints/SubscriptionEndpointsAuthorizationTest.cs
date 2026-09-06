using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription capability is per-shopper, so every route must be behind the bearer token. These
/// assertions do not reach the billing provider: authorization is settled before the handler runs.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthorizationTest
{
    [TestMethod]
    public async Task ListPlansRequiresAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsRequiresAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresAToken()
    {
        var response = await ProgramTest.NewClient.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsAForgedToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
