using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints act on whoever the bearer token says the caller is, so an
/// unauthenticated request must never reach the billing provider. These assertions hold with or
/// without Maxio configured: authorization runs before the capability is resolved.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthorizationTest
{
    [TestMethod]
    public async Task ListingPlansRequiresAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListingMySubscriptionsRequiresAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingRequiresAToken()
    {
        var response = await ProgramTest.NewClient.PostAsJsonAsync(
            "/api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingWithoutAPlanHandleIsRejectedBeforeAnyBillingCall()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiTokenHelper.GetNormalUserToken()}");

        var response = await client.PostAsJsonAsync("/api/subscriptions", new { });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
