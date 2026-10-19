using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The test host points Maxio:BaseUrl at an unroutable address (appsettings.test.json), so these tests
/// exercise authentication and error mapping without ever reaching a real Maxio site.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    [DataTestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task ReturnsUnauthorizedWithoutToken(string route)
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.PostAsync("api/subscriptions", PlanJson("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeReturnsBadRequestWithoutPlanHandle()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsServiceUnavailableWhenBillingProviderIsUnreachable()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.IsTrue(response.Headers.Contains("Retry-After"));
        // Caller-safe message only: no SDK types or provider URLs leak.
        Assert.IsFalse(body.Contains("MaxioAdvancedBilling"));
        Assert.IsFalse(body.Contains("127.0.0.1"));
    }

    private static StringContent PlanJson(string planHandle) =>
        new($"{{\"planHandle\":\"{planHandle}\"}}", Encoding.UTF8, "application/json");
}
