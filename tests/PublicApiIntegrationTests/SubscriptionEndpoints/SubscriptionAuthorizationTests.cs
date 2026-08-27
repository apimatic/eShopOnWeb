using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionAuthorizationTests
{
    [DataTestMethod]
    [DataRow("/api/subscription-plans")]
    [DataRow("/api/my-subscriptions")]
    public async Task GetEndpointsRejectAnonymousCallers(string path)
    {
        using var response = await ProgramTest.NewClient.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsAnonymousCallers()
    {
        using var response = await ProgramTest.NewClient.PostAsJsonAsync(
            "/api/subscriptions",
            new { productHandle = "not-sent-to-provider" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
