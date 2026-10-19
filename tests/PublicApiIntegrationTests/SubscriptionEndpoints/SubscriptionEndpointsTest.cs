using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("GET", "api/my-subscriptions")]
    [DataRow("POST", "api/subscriptions")]
    public async Task RequiresBearerToken(string method, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
        {
            request.Content = new StringContent("""{"planHandle":"any"}""", Encoding.UTF8, "application/json");
        }

        var response = await ProgramTest.NewClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task UnreachableBillingProviderIsReportedAsServiceUnavailable()
    {
        // The test configuration points Maxio:BaseUrl at an address nothing listens on.
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "billing provider");
        Assert.IsFalse(body.Contains("127.0.0.1"), "provider address must not leak to the caller");
    }
}
