using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Auth-surface tests for the subscription endpoints. The billing provider itself is
/// never touched: an unauthenticated caller must be rejected before any billing work.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthTests
{
    [TestMethod]
    public async Task GetSubscriptionPlans_ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMySubscriptions_ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var jsonContent = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/subscriptions", jsonContent);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}