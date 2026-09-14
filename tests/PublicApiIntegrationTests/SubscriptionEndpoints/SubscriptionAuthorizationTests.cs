using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
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
        var response = await ProgramTest.NewClient.GetAsync(path);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeEndpointRejectsAnonymousCallers()
    {
        using var body = new StringContent("{\"productHandle\":\"pro\"}", Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", body);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
