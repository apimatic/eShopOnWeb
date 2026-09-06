using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints act on the caller's own billing records, so an unauthenticated caller
/// must never reach them. These assertions hold without any Maxio configuration: authorization runs
/// before the endpoint, so nothing here talks to a billing site.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthorizationTest
{
    [TestMethod]
    public async Task ListPlansRequiresABearerToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsRequiresABearerToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresABearerToken()
    {
        var response = await ProgramTest.NewClient.PostAsJsonAsync(
            "api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsATamperedBearerToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not.a.valid.token");

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
