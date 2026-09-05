using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints call out to Maxio, so these tests only exercise the piece
/// that doesn't require a live sandbox: that every endpoint demands a valid bearer token.
/// The end-to-end subscribe flow is verified manually against the Maxio sandbox (see the
/// verification guide) since it depends on external, stateful billing data.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthTest
{
    [TestMethod]
    public async Task ListSubscriptionPlans_ReturnsUnauthorized_GivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_ReturnsUnauthorized_GivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var content = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
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
