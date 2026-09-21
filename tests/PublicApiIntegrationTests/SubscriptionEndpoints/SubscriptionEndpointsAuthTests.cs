using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Verifies the subscription endpoints are wired under <c>/api/</c> and JWT-protected. These assert the auth
/// boundary only (no bearer token) so they need no billing-provider connectivity; the full subscribe flow is
/// covered by the Infrastructure unit tests and by manual verification against the Maxio sandbox.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthTests
{
    [TestMethod]
    public async Task GetSubscriptionPlans_without_token_is_unauthorized()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("/api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMySubscriptions_without_token_is_unauthorized()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("/api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PostSubscriptions_without_token_is_unauthorized()
    {
        var client = ProgramTest.NewClient;
        var content = new StringContent("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/subscriptions", content);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
