using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Covers the wiring of the subscription endpoints: routes, authentication and the plan projection.
/// </summary>
/// <remarks>
/// The read-only plan test calls the configured Maxio site, so it reports inconclusive when the
/// host has no Maxio credentials rather than failing a build that never had them. Nothing here
/// writes to the billing provider — the enrolment rules are covered by the unit tests, which run
/// against an in-memory Maxio and need no credentials.
/// </remarks>
[TestClass]
public class SubscriptionEndpointsTest
{
    [DataTestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetRequiresABearerToken(string route)
    {
        var response = await ProgramTest.NewClient.GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresABearerToken()
    {
        var response = await ProgramTest.NewClient.PostAsync(
            "api/subscriptions",
            new StringContent("{\"planHandle\":\"pro-plan\"}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlansGivenANormalUserToken()
    {
        SkipWhenMaxioIsNotConfigured();

        var client = AuthenticatedClient();

        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();

        Assert.IsNotNull(model);
        CollectionAssert.AllItemsAreNotNull(model!.SubscriptionPlans);
        Assert.IsTrue(model.SubscriptionPlans.All(p => !string.IsNullOrWhiteSpace(p.Handle)),
            "Every plan must expose the handle callers subscribe with.");
        Assert.IsTrue(model.SubscriptionPlans.SequenceEqual(model.SubscriptionPlans.OrderBy(p => p.PriceInCents)),
            "Plans are expected cheapest first.");
    }

    [TestMethod]
    public async Task RejectsASubscribeRequestWithNoPlanHandle()
    {
        var client = AuthenticatedClient();

        var response = await client.PostAsync(
            "api/subscriptions", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpClient AuthenticatedClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static void SkipWhenMaxioIsNotConfigured()
    {
        var configuration = ProgramTest.Services.GetRequiredService<IConfiguration>();

        if (string.IsNullOrWhiteSpace(configuration["Maxio:ApiKey"]))
        {
            Assert.Inconclusive(
                "Maxio is not configured on this host; set Maxio:ApiKey, Maxio:Subdomain and " +
                "Maxio:ProductFamilyHandle (user-secrets or environment) to run this test.");
        }
    }
}
