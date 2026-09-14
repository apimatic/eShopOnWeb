using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionAuthorizationTests
{
    [DataTestMethod]
    [DataRow("/api/subscription-plans")]
    [DataRow("/api/my-subscriptions")]
    public async Task GetEndpointsRejectRequestsWithoutBearerToken(string route)
    {
        var response = await ProgramTest.NewClient.GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsRequestsWithoutBearerToken()
    {
        var response = await ProgramTest.NewClient.PostAsync("/api/subscriptions", null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
