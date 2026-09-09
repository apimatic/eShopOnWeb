using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointRoutingTests
{
    [TestMethod]
    public async Task GetPlansAnonymousReturns401()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, $"status={(int)response.StatusCode} body={await response.Content.ReadAsStringAsync()}");
    }

    [TestMethod]
    public async Task PostSubscriptionsAnonymousReturns401()
    {
        var client = ProgramTest.NewClient;
        var content = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/subscriptions", content);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, $"status={(int)response.StatusCode} body={await response.Content.ReadAsStringAsync()}");
    }

    [TestMethod]
    public async Task GetMySubscriptionsAnonymousReturns401()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, $"status={(int)response.StatusCode} body={await response.Content.ReadAsStringAsync()}");
    }
}
