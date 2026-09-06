using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription capability is for signed-in shoppers, and the shopper identity is taken from
/// the bearer token rather than from the request. These tests hold that line without calling the
/// billing provider, so they stay deterministic and run offline.
/// </summary>
[TestClass]
public class SubscriptionEndpointsRequireAuthentication
{
    private readonly HttpClient _client = ProgramTest.NewClient;

    [TestMethod]
    public async Task ListingPlansWithoutATokenIsUnauthorized()
    {
        var response = await _client.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListingMySubscriptionsWithoutATokenIsUnauthorized()
    {
        var response = await _client.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingWithoutATokenIsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingWithAnInvalidTokenIsUnauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/subscriptions")
        {
            Content = JsonContent.Create(new { planHandle = "eshop-pro" })
        };
        request.Headers.Add("Authorization", "Bearer not-a-real-token");

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
