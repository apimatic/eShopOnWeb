using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionAuthorizationTests
{
    [TestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetRequiresBearerToken(string route)
    {
        var response = await ProgramTest.NewClient.GetAsync(route);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresBearerToken()
    {
        var response = await ProgramTest.NewClient.PostAsJsonAsync("api/subscriptions",
            new { productHandle = "eshop-pro" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
