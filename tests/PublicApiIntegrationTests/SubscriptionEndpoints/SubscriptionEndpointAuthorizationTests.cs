using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointAuthorizationTests
{
    [TestMethod]
    public async Task ListPlans_ReturnsUnauthorized_WithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_ReturnsUnauthorized_WithoutToken()
    {
        var json = new StringContent("""{"productHandle":"eshop-pro"}""", Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", json);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptions_ReturnsUnauthorized_WithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
