using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Boots the PublicApi host (which now performs Maxio startup validation against the placeholder
/// configuration in appsettings.test.json) and verifies the subscription endpoints require a JWT.
/// The host booting green is itself the assertion that the fail-fast credential check passes with
/// the test host's placeholder configuration.
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
    public async Task PostSubscription_WithoutToken_IsUnauthorized()
    {
        var client = ProgramTest.NewClient;
        var body = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/subscriptions", body);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
