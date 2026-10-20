using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The test host's Maxio settings are fake placeholders pointing at an unresolvable host (appsettings.test.json),
/// so these tests never reach Maxio. Flow behaviour against a stubbed Maxio is covered in tests/UnitTests.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("POST", "api/subscriptions")]
    [DataRow("GET", "api/my-subscriptions")]
    public async Task ReturnsUnauthorizedWithoutToken(string method, string route)
    {
        var client = ProgramTest.NewClient;
        var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
        {
            request.Content = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsServiceUnavailableWithoutLeakingDetailsWhenMaxioIsUnreachable()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(body, "could not be reached");
        Assert.IsFalse(body.Contains("maxio.test.invalid"), "the provider URL must not reach the caller");
    }
}
