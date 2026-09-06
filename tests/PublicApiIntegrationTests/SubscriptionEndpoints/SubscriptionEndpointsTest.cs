using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Covers what the subscription endpoints must do without talking to a billing provider: refuse
/// anonymous callers, validate input, and report an unconfigured deployment honestly.
/// </summary>
/// <remarks>
/// These tests deliberately run against a host whose Maxio configuration has been blanked, so they
/// never reach the network and never depend on the state of a sandbox site.
/// </remarks>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static WebApplicationFactory<Program> _unconfiguredBilling = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _unconfiguredBilling = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Maxio:ApiKey"] = string.Empty,
                    ["Maxio:Subdomain"] = string.Empty,
                    ["Maxio:BaseUrl"] = string.Empty,
                    ["Maxio:ProductFamilyHandle"] = string.Empty
                })));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _unconfiguredBilling?.Dispose();

    [DataTestMethod]
    [DataRow("subscription-plans")]
    [DataRow("my-subscriptions")]
    public async Task AnonymousCallersCannotReadSubscriptionData(string endpointName)
    {
        var response = await ProgramTest.NewClient.GetAsync($"/api/{endpointName}");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AnonymousCallersCannotSubscribe()
    {
        var response = await ProgramTest.NewClient.PostAsync("/api/subscriptions", JsonBody("{\"planHandle\":\"eshop-pro\"}"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingWithoutAPlanHandleIsRejectedBeforeBillingIsCalled()
    {
        var client = AuthenticatedClient();

        var response = await client.PostAsync("/api/subscriptions", JsonBody("{}"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "planHandle is required");
    }

    [DataTestMethod]
    [DataRow("subscription-plans")]
    [DataRow("my-subscriptions")]
    public async Task ReadsReportAnUnconfiguredDeploymentAsUnavailable(string endpointName)
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync($"/api/{endpointName}");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        // An operator has to learn which keys to supply, and never a value.
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "Maxio:ApiKey");
        StringAssert.Contains(body, "Maxio:ProductFamilyHandle");
    }

    [TestMethod]
    public async Task SubscribingReportsAnUnconfiguredDeploymentAsUnavailable()
    {
        var client = AuthenticatedClient();

        var response = await client.PostAsync("/api/subscriptions", JsonBody("{\"planHandle\":\"eshop-pro\"}"));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static HttpClient AuthenticatedClient()
    {
        var client = _unconfiguredBilling.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        return client;
    }

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");
}
