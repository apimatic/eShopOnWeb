using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints are billing operations keyed off the caller's identity, so an unauthenticated
/// request must never reach the billing system. These assertions need no Maxio configuration, by design.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthTest
{
    [TestMethod]
    public async Task ListPlansRequiresABearerToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsRequiresABearerToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("/api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresABearerToken()
    {
        var content = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");

        var response = await ProgramTest.NewClient.PostAsync("/api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
