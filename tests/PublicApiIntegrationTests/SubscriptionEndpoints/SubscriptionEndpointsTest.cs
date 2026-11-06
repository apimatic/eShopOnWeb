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
    // Billing is pointed at an unreachable address by appsettings.test.json: these tests never reach Maxio.

    private static HttpClient AuthorizedClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("GET", "api/my-subscriptions")]
    [DataRow("POST", "api/subscriptions")]
    public async Task RequireAuthentication(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = new StringContent("{\"planHandle\":\"x\"}", Encoding.UTF8, "application/json");
        }

        var response = await ProgramTest.NewClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsMissingPlanHandle()
    {
        var response = await AuthorizedClient().PostAsync("api/subscriptions",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UnreachableProviderIsABadGatewayWithoutLeakingSdkDetail()
    {
        var response = await AuthorizedClient().GetAsync("api/subscription-plans");
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.IsFalse(body.Contains("MaxioAdvancedBilling"), body);
        Assert.IsFalse(body.Contains("127.0.0.1"), body);
    }
}
