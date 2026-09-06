using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Covers the wiring of the subscription endpoints: the routes exist, they demand a bearer token,
/// and a missing billing configuration is reported as an unavailable capability rather than as a
/// server fault.
/// <para>
/// The host used here has the <c>Maxio</c> settings blanked out, so no test ever reaches - or
/// changes anything in - the billing provider. The behaviour against a configured Maxio site is
/// covered by the unit tests, which drive the client through a stub transport.
/// </para>
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly UnconfiguredBillingApplicationFactory Application = new();

    [DataTestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetRequiresABearerToken(string route)
    {
        var response = await Application.CreateClient().GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresABearerToken()
    {
        var response = await Application.CreateClient()
            .PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetReportsBillingUnavailableWhenMaxioIsNotConfigured(string route)
    {
        var response = await NewAuthenticatedClient().GetAsync(route);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeReportsBillingUnavailableWhenMaxioIsNotConfigured()
    {
        var response = await NewAuthenticatedClient()
            .PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static HttpClient NewAuthenticatedClient()
    {
        var client = Application.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        return client;
    }

    /// <summary>
    /// A host whose Maxio settings are deliberately empty, overriding anything a developer machine
    /// may hold in user-secrets or environment variables.
    /// </summary>
    private sealed class UnconfiguredBillingApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
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
