using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointAuthenticationTests
{
    [DataTestMethod]
    [DataRow("/api/subscription-plans")]
    [DataRow("/api/my-subscriptions")]
    public async Task GetEndpointsRequireBearerToken(string path)
    {
        var response = await ProgramTest.NewClient.GetAsync(path);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateEndpointRequiresBearerToken()
    {
        var response = await ProgramTest.NewClient.PostAsync("/api/subscriptions", null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
