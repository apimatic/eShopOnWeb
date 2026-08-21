using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointAuthTests
{
    [TestMethod]
    public async Task ListPlans_ReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_ReturnsUnauthorizedWithoutToken()
    {
        var body = new StringContent("""{"productHandle":"eshop-pro"}""", Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", body);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptions_ReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlans_ReturnsOkForAuthenticatedShopper()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        var plans = document.RootElement.GetProperty("subscriptionPlans");
        Assert.IsTrue(plans.GetArrayLength() >= 1);
    }
}
