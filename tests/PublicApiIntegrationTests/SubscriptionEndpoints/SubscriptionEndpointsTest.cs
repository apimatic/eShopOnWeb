using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task ListSubscriptionPlansReturnsUnauthorizedGivenNoToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionReturnsUnauthorizedGivenNoToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = null;
        var jsonContent = new StringContent("{\"productHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/subscriptions", jsonContent);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsReturnsUnauthorizedGivenNoToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
