using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints act on the caller's own billing records, so every one of them has to
/// refuse an anonymous request. These assertions need no Maxio credentials: authorization runs before
/// the endpoint touches the billing system.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthorizationTest
{
    [TestMethod]
    public async Task ListPlansRequiresAToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsRequiresAToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresAToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The plan handle is validated before the billing system is contacted, so this stays a pure
    /// contract check even on a host with no Maxio credentials.
    /// </summary>
    [TestMethod]
    public async Task SubscribeRejectsARequestWithNoPlanHandle()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
