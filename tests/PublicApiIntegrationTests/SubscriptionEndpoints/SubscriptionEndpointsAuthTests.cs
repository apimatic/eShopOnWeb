using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsAuthTests
{
    [DataTestMethod]
    [DataRow("/api/subscription-plans")]
    [DataRow("/api/my-subscriptions")]
    public async Task GetEndpointsRequireJwt(string path)
    {
        var response = await ProgramTest.NewClient.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionRequiresJwt()
    {
        var response = await ProgramTest.NewClient.PostAsync(
            "/api/subscriptions",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
