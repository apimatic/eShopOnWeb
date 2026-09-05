using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// These tests exercise the real Maxio sandbox (site/credentials come from user-secrets - see
/// Maxio:ApiKey / Maxio:Subdomain / Maxio:ProductFamilyHandle) rather than a fake, so they
/// double as a live contract check against maxio-spec/.
/// </summary>
[TestClass]
public class ListSubscriptionPlansEndpointTest
{
    [TestMethod]
    public async Task ReturnsTheSeededPlansWhenAuthenticated()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("/api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>();

        Assert.IsNotNull(body);
        Assert.IsTrue(body!.Plans.Any(p => p.PlanHandle == "eshop-pro"), "Expected the seeded Pro Plan (handle eshop-pro) to be listed.");
        Assert.IsTrue(body.Plans.Any(p => p.PlanHandle == "basic-plan"), "Expected the seeded Basic Plan (handle basic-plan) to be listed.");
        Assert.IsTrue(body.Plans.All(p => p.PriceInCents > 0));
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("/api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
