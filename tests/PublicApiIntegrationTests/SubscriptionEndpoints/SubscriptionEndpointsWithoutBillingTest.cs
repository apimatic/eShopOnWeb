using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Missing billing credentials must degrade the subscription endpoints only - never stop the
/// application from starting or disturb the one-time commerce flow.
/// </summary>
[TestClass]
public class SubscriptionEndpointsWithoutBillingTest
{
    private static UnconfiguredBillingApplication _application = new();

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _application = new UnconfiguredBillingApplication();

    [ClassCleanup]
    public static void ClassCleanup() => _application.Dispose();

    [TestMethod]
    public async Task ListPlansReportsTheCapabilityAsUnavailable()
    {
        var response = await AuthenticatedClient().GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "not configured");
    }

    [TestMethod]
    public async Task SubscribeReportsTheCapabilityAsUnavailable()
    {
        var content = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");

        var response = await AuthenticatedClient().PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsARequestWithoutAPlanHandleBeforeTouchingBilling()
    {
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await AuthenticatedClient().PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "PlanHandle");
    }

    [TestMethod]
    public async Task TheOneTimeCommerceFlowStillWorks()
    {
        var response = await AuthenticatedClient().GetAsync("api/catalog-items");

        response.EnsureSuccessStatusCode();
    }

    private static HttpClient AuthenticatedClient()
    {
        var client = _application.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        return client;
    }
}
