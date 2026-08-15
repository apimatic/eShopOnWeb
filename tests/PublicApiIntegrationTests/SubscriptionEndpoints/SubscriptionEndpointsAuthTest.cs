using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints are JWT-protected. These tests assert the auth gate without touching the
/// billing provider — an unauthenticated caller is rejected before any handler (or Maxio call) runs.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthTest
{
    [TestMethod]
    public async Task GetSubscriptionPlans_WithoutToken_IsUnauthorized()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMySubscriptions_WithoutToken_IsUnauthorized()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_WithoutToken_IsUnauthorized()
    {
        var client = ProgramTest.NewClient;
        var body = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/subscriptions", body);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
