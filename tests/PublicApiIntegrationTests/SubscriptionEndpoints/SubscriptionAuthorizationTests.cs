using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionAuthorizationTests
{
    [DataTestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetRequiresBearerToken(string path)
    {
        using var client = ProgramTest.NewClient;

        var response = await client.GetAsync(path);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateRequiresBearerToken()
    {
        using var client = ProgramTest.NewClient;
        using var content = new StringContent(
            "{\"productHandle\":\"portable-plan\"}",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
