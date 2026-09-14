using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Covers the parts of the subscription endpoints that do not depend on a live billing system:
/// that they are mapped, that they demand a bearer token, and that a host without billing
/// configured degrades to 503 instead of taking the rest of the API down with it.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    /// <summary>
    /// A PublicApi host with the Maxio section explicitly blank, so the assertions below do not
    /// depend on whatever credentials the developer running the tests happens to have configured.
    /// </summary>
    private sealed class UnconfiguredBillingApplication : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Maxio:ApiKey"] = "",
                    ["Maxio:Subdomain"] = "",
                    ["Maxio:BaseUrl"] = "",
                    ["Maxio:ProductFamilyHandle"] = ""
                }));

            return base.CreateHost(builder);
        }
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> application)
    {
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        return client;
    }

    [TestMethod]
    public async Task ListPlansRequiresABearerToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsRequiresABearerToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresABearerToken()
    {
        var response = await ProgramTest.NewClient.PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlansReportsServiceUnavailableWhenBillingIsNotConfigured()
    {
        using var application = new UnconfiguredBillingApplication();

        var response = await AuthenticatedClient(application).GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Maxio:ApiKey");
    }

    [TestMethod]
    public async Task SubscribeReportsServiceUnavailableWhenBillingIsNotConfigured()
    {
        using var application = new UnconfiguredBillingApplication();

        var response = await AuthenticatedClient(application)
            .PostAsJsonAsync("api/subscriptions", new { planHandle = "eshop-pro" });

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    public async Task TheRestOfTheApiKeepsWorkingWhenBillingIsNotConfigured()
    {
        using var application = new UnconfiguredBillingApplication();

        // A misconfigured billing section must not stop the host from starting or break catalog reads.
        var response = await application.CreateClient().GetAsync("api/catalog-items");

        response.EnsureSuccessStatusCode();
    }
}
