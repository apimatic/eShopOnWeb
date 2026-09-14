using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints must be JWT-authenticated - callers with no token are rejected before
/// ever reaching the Maxio integration, so these run with no dependency on Maxio connectivity.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthTest
{
    [TestMethod]
    public async Task ListPlans_ReturnsUnauthorized_GivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_ReturnsUnauthorized_GivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var content = new StringContent("{\"planHandle\":\"eshop-pro\"}", System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptions_ReturnsUnauthorized_GivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
