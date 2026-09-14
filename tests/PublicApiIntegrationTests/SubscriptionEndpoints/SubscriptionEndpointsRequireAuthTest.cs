using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Verifies the subscription endpoints are registered and JWT-protected. These tests never reach the
/// Maxio provider: an unauthenticated request is rejected by [Authorize] before any controller work.
/// </summary>
[TestClass]
public class SubscriptionEndpointsRequireAuthTest
{
    [DataTestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("GET", "api/my-subscriptions")]
    public async Task ReturnsUnauthorizedWithoutToken(string method, string url)
    {
        var client = ProgramTest.NewClient;

        var request = new HttpRequestMessage(new HttpMethod(method), url);
        var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PostSubscriptionsReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var content = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
