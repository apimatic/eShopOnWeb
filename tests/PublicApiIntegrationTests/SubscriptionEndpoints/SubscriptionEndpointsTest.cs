using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Covers the parts of the subscription capability that hold regardless of whether a billing provider is
/// reachable: the endpoints are authenticated, and a deployment without billing configured degrades to a
/// clear 503 on those three routes instead of failing to start or answering a generic 500.
///
/// The hero flow itself is verified against the Maxio sandbox rather than here, so the suite stays
/// hermetic and does not bill anyone when CI runs.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly string[] Routes = { "api/subscription-plans", "api/my-subscriptions" };

    private static WebApplicationFactory<Program> _unconfigured = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        // Blank out the Maxio section last so ambient MAXIO_* variables on a developer machine cannot
        // turn this into a test that quietly calls the real billing provider.
        _unconfigured = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Maxio:ApiKey"] = string.Empty,
                    ["Maxio:Subdomain"] = string.Empty,
                    ["Maxio:ProductFamilyHandle"] = string.Empty,
                    ["Maxio:BaseUrl"] = string.Empty,
                })));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _unconfigured.Dispose();

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
        var response = await ProgramTest.NewClient.PostAsJsonAsync("api/subscriptions", new { planHandle = "any-plan" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReadEndpointsReportUnavailableWhenBillingIsNotConfigured()
    {
        var client = CreateAuthenticatedClient();

        foreach (var route in Routes)
        {
            var response = await client.GetAsync(route);

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, $"Route {route}");
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Maxio:ApiKey", $"Route {route}");
        }
    }

    [TestMethod]
    public async Task SubscribeReportsUnavailableWhenBillingIsNotConfigured()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("api/subscriptions", new { planHandle = "any-plan" });

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Maxio:ApiKey");
    }

    [TestMethod]
    public async Task UnconfiguredBillingDoesNotBreakTheRestOfTheApi()
    {
        var response = await _unconfigured.CreateClient().GetAsync("api/catalog-brands");

        response.EnsureSuccessStatusCode();
    }

    private static HttpClient CreateAuthenticatedClient()
    {
        var client = _unconfigured.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        return client;
    }
}
