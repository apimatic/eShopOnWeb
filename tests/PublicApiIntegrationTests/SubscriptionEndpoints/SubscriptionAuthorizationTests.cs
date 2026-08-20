using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionAuthorizationTests
{
    [DataTestMethod]
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
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions",
            JsonContent.Create(new { productHandle = "any-plan" }));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
