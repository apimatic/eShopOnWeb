using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

/// <summary>
/// Boots the PublicApi host (which fails fast on missing Maxio:* settings) with the placeholder Maxio
/// configuration from appsettings.test.json, proving the startup credential check passes and the
/// subscription endpoints are wired with JWT auth — without making any real Maxio call.
/// </summary>
[TestClass]
public class SubscriptionEndpointsSmokeTest
{
    [TestMethod]
    public async Task SubscriptionPlans_WithoutToken_IsRejected()
    {
        HttpClient client = ProgramTest.NewClient; // building this boots the host

        HttpResponseMessage response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptions_WithoutToken_IsRejected()
    {
        HttpClient client = ProgramTest.NewClient;

        HttpResponseMessage response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
