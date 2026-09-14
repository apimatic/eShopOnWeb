using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointAuthorizationTests
{
    [TestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetRequiresJwt(string path)
    {
        var response = await ProgramTest.NewClient.GetAsync(path);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresJwt()
    {
        var response = await ProgramTest.NewClient.PostAsJsonAsync(
            "api/subscriptions",
            new CreateSubscriptionRequest { ProductHandle = "basic" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
