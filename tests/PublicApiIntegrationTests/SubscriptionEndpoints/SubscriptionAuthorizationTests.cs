using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionAuthorizationTests
{
    [DataTestMethod]
    [DataRow("/api/subscription-plans")]
    [DataRow("/api/my-subscriptions")]
    public async Task GetEndpointsRequireJwt(string path)
    {
        var response = await ProgramTest.NewClient.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
