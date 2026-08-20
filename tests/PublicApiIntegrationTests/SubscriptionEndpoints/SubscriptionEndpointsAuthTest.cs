using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsAuthTest
{
    [TestMethod]
    public async Task ListPlans_ReturnsUnauthorized_WithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptions_ReturnsUnauthorized_WithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_ReturnsUnauthorized_WithoutToken()
    {
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
