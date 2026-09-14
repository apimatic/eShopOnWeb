using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class SubscriptionEndpointAuthTests
{
    [TestMethod]
    public async Task SubscriptionPlansRequireAuthentication()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionRequiresAuthentication()
    {
        var request = new StringContent("{\"productHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");

        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsRequireAuthentication()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
