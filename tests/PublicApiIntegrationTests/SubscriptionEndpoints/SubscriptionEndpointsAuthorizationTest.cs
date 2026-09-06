using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints take the shopper's identity from the bearer token, so an anonymous caller
/// must never reach the billing system at all.
/// </summary>
/// <remarks>
/// These assertions stop short of the billing system on purpose: the test host is configured with a
/// placeholder Advanced Billing site, so nothing here makes an outbound call.
/// </remarks>
[TestClass]
public class SubscriptionEndpointsAuthorizationTest
{
    [TestMethod]
    public async Task ListPlansRejectsAnAnonymousCaller()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsRejectsAnAnonymousCaller()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsAnAnonymousCaller()
    {
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", SubscribeBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsAGarbageToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var response = await client.PostAsync("api/subscriptions", SubscribeBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRejectsAnOverlongIdempotencyKeyBeforeReachingBilling()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        client.DefaultRequestHeaders.Add("Idempotency-Key", new string('k', 200));

        var response = await client.PostAsync("api/subscriptions", SubscribeBody());

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static StringContent SubscribeBody() =>
        new("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
}
