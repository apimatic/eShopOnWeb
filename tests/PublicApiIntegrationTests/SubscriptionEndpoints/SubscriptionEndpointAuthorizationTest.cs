using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointAuthorizationTest
{
    [TestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetEndpointsRequireBearerToken(string route)
    {
        var response = await ProgramTest.NewClient.GetAsync(route);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateEndpointRequiresBearerToken()
    {
        using var content = new StringContent("{\"productHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", content);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
