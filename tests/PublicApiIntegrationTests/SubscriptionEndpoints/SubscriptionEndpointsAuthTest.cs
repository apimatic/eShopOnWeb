using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints are JWT-protected; an unauthenticated caller must be rejected before
/// any billing work happens. These assertions need no live Maxio connectivity (authorization
/// short-circuits ahead of the service).
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthTest
{
    [TestMethod]
    public async Task GetSubscriptionPlans_ReturnsUnauthorized_WithoutToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMySubscriptions_ReturnsUnauthorized_WithoutToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_ReturnsUnauthorized_WithoutToken()
    {
        var client = ProgramTest.NewClient;
        var body = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/subscriptions", body);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
