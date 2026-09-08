using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpoints
{
    private static HttpClient GetClient()
    {
        return ProgramTest.NewClient;
    }

    [TestMethod]
    public async Task SubscriptionPlansRejectAnonymousRequest()
    {
        var client = GetClient();
        var response = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionRejectsAnonymousRequest()
    {
        var client = GetClient();
        var response = await client.PostAsync("api/subscriptions",
            new StringContent("{\"planHandle\":\"eshop-pro\"}", System.Text.Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsRejectAnonymousRequest()
    {
        var client = GetClient();
        var response = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
