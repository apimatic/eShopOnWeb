using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Contract tests for the subscription endpoints that do not reach the billing provider: routing,
/// authentication, and request validation. The behaviour behind them is covered by the unit tests
/// in UnitTests/Infrastructure/Billing/Maxio, which run against a fake provider.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthTest
{
    [DataTestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetEndpointsRequireABearerToken(string route)
    {
        var response = await ProgramTest.NewClient.GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresABearerToken()
    {
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", JsonBody("{\"planHandle\":\"eshop-pro\"}"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsARequestWithNoPlanHandleBeforeCallingTheProvider()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", JsonBody("{}"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "planHandle is required");
    }

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");
}
