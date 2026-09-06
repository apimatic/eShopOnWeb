using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints are the only place where a caller can act on billing data, so the guard that
/// matters most is that they are unreachable without a bearer token. The 401 cases never reach the billing
/// provider; the "not configured" case runs against a host whose Maxio settings are explicitly blanked, so
/// the test does not depend on whatever credentials the machine happens to hold.
/// </summary>
[TestClass]
public class SubscriptionEndpointsSecurityTest
{
    private static StringContent SubscribeBody() =>
        new("{\"planHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");

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
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", SubscribeBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeIsRejectedWhenTheTokenIsNotSignedByThisApi()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var response = await client.PostAsync("api/subscriptions", SubscribeBody());

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AnAuthenticatedCallReportsThatBillingIsNotConfiguredRatherThanFailing()
    {
        using var application = new UnconfiguredBillingApplication();
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        // The capability must degrade to a clear "service unavailable" instead of a 500 ...
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Maxio:ApiKey");

        // ... and must not take the rest of the API down with it.
        var catalog = await application.CreateClient().GetAsync("api/catalog-brands");
        Assert.AreEqual(HttpStatusCode.OK, catalog.StatusCode);
    }

    private sealed class UnconfiguredBillingApplication : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Maxio:ApiKey"] = string.Empty,
                    ["Maxio:Subdomain"] = string.Empty,
                    ["Maxio:BaseUrl"] = string.Empty,
                    ["Maxio:ProductFamilyHandle"] = string.Empty
                }));
        }
    }
}
