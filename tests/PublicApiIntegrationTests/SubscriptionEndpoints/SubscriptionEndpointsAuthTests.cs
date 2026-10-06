using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

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
    public async Task CreateSubscription_ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.PostAsync("api/subscriptions", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMySubscriptions_ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}