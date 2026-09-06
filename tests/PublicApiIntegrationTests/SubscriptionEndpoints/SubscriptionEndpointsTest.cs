using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Covers the parts of the subscription endpoints that do not depend on a live billing provider:
/// routing, authentication and error mapping. The test host points at an unreachable base address
/// (see appsettings.test.json), so any call that would reach Maxio surfaces as a gateway failure.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static HttpClient Anonymous() => ProgramTest.NewClient;

    private static HttpClient Authenticated()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        return client;
    }

    [TestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task ReadEndpointsRequireABearerToken(string route)
    {
        var response = await Anonymous().GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresABearerToken()
    {
        var response = await Anonymous().PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsAMissingPlanHandleWithoutCallingTheProvider()
    {
        var response = await Authenticated().PostAsJsonAsync("api/subscriptions", new { });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "planHandle is required");
    }

    [TestMethod]
    public async Task AnUnreachableBillingProviderIsReportedAsAGatewayFailure()
    {
        var response = await Authenticated().GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(502, body.RootElement.GetProperty("StatusCode").GetInt32());
    }

    [TestMethod]
    public async Task SubscribingWhileTheProviderIsUnreachableIsAlsoAGatewayFailure()
    {
        var response = await Authenticated().PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
    }
}
