using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Covers the parts of the subscription endpoints that do not talk to the billing provider: every
/// route is behind the bearer token, and the request contract is validated before anything is billed.
/// The provider-facing behaviour is covered by the unit tests, which do not need live credentials.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthorizationTest
{
    private static StringContent SubscribeBody(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private static HttpClient AuthenticatedClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        return client;
    }

    [TestMethod]
    public async Task ListPlansRequiresAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsRequiresAToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresAToken()
    {
        var response = await ProgramTest.NewClient.PostAsync(
            "api/subscriptions", SubscribeBody("""{"planHandle":"eshop-pro"}"""));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsAnInvalidTokenRatherThanTrustingTheBody()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        var response = await client.PostAsync("api/subscriptions", SubscribeBody("""{"planHandle":"eshop-pro"}"""));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsARequestWithoutAPlanHandleBeforeCallingTheProvider()
    {
        var response = await AuthenticatedClient().PostAsync("api/subscriptions", SubscribeBody("{}"));

        // 400 is the validation failure. A 503 here would mean billing configuration was consulted
        // first, which would make the endpoint unusable on a host without Maxio credentials.
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "plan handle is required");
    }
}
