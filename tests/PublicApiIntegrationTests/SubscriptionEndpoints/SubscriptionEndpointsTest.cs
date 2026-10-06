using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Hermetic tests for the subscription endpoints (no Maxio credentials required):
/// they only exercise authentication and input validation, which happen before
/// any call to the billing system.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static HttpClient GetClient() => ProgramTest.NewClient;

    private static HttpClient GetAuthenticatedClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task SubscriptionPlansRequireAuthentication()
    {
        var response = await GetClient().GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsRequireAuthentication()
    {
        var response = await GetClient().GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionRequiresAuthentication()
    {
        var response = await PostJsonAsync(GetClient(), new { planHandle = "eshop-pro" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionRejectsMissingPlanHandle()
    {
        var response = await PostJsonAsync(GetAuthenticatedClient(), new { });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, object payload) =>
        client.PostAsync("api/subscriptions",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
}

