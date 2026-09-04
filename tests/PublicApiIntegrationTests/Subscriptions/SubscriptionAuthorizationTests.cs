using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class SubscriptionAuthorizationTests
{
    [TestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetSubscriptionEndpointsRequireJwt(string route)
    {
        var response = await ProgramTest.NewClient.GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionRequiresJwt()
    {
        using var content = new StringContent("{\"planHandle\":\"eshop-pro\"}", System.Text.Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
