using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// All three subscription endpoints require a JWT - verify they reject anonymous callers.
/// (The success paths were verified end-to-end against the live Maxio sandbox rather than here,
/// so the suite doesn't depend on external billing credentials being configured.)
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthorizationTest
{
    [TestMethod]
    public async Task GetSubscriptionPlans_ReturnsUnauthorized_GivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_ReturnsUnauthorized_GivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var jsonContent = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/subscriptions", jsonContent);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMySubscriptions_ReturnsUnauthorized_GivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
