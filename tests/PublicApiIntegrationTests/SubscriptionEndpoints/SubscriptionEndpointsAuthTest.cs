using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints are JWT-protected. These tests assert the authorization guard and routing
/// without reaching the external billing provider (an unauthenticated request short-circuits at 401).
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthTest
{
    private static readonly HttpClient _client = ProgramTest.NewClient;

    [TestMethod]
    public async Task GetSubscriptionPlans_ReturnsUnauthorized_WhenNoToken()
    {
        var response = await _client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PostSubscriptions_ReturnsUnauthorized_WhenNoToken()
    {
        var body = new StringContent("{\"planHandle\":\"eshop-pro\"}", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("api/subscriptions", body);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMySubscriptions_ReturnsUnauthorized_WhenNoToken()
    {
        var response = await _client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
