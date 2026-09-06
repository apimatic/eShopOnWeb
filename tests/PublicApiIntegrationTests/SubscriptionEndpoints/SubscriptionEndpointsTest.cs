using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Covers the parts of the subscription contract that hold regardless of the billing system:
/// who may call the endpoints, and what an incomplete request gets back. Anything past that
/// point talks to Maxio, so it is verified against the sandbox rather than in this suite.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    [DataTestMethod]
    [DataRow("subscription-plans")]
    [DataRow("my-subscriptions")]
    public async Task ReturnsUnauthorizedGivenNoTokenOnReads(string endpointName)
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync($"/api/{endpointName}");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedGivenNoTokenOnSubscribe()
    {
        var client = ProgramTest.NewClient;

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsBadRequestGivenNoPlanHandle()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        // The plan is missing, so the request is rejected before the billing system is contacted.
        var response = await client.PostAsJsonAsync("/api/subscriptions", new { });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
