using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionAuthorizationTests
{
    [TestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetEndpointsRejectAnonymousCallers(string path)
    {
        using var response = await ProgramTest.NewClient.GetAsync(path);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsAnonymousCallers()
    {
        using var response = await ProgramTest.NewClient.PostAsJsonAsync("api/subscriptions",
            new CreateSubscriptionRequest { ProductHandle = "a-plan" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
