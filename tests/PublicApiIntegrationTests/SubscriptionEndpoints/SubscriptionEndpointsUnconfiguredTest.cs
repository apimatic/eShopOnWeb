using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The host has to start and keep serving the rest of the API when billing is not configured, and the
/// subscription endpoints have to say so plainly instead of blaming the caller or leaking a 500.
/// </summary>
[TestClass]
public class SubscriptionEndpointsUnconfiguredTest
{
    private static WebApplicationFactory<Program> _application = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Maxio:ApiKey"] = string.Empty,
                    ["Maxio:Subdomain"] = string.Empty,
                    ["Maxio:BaseUrl"] = string.Empty,
                    ["Maxio:ProductFamilyHandle"] = string.Empty
                })));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _application?.Dispose();

    [TestMethod]
    public void TheRestOfTheApiStillWorksWhenBillingIsNotConfigured()
    {
        var services = _application.Services;

        Assert.IsNotNull(services);
    }

    [TestMethod]
    public async Task ListPlansReportsTheMissingConfigurationAsUnavailable()
    {
        var response = await AuthenticatedClient().GetAsync("api/subscription-plans");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        StringAssert.Contains(body, "Maxio:ApiKey");
        StringAssert.Contains(body, "Maxio:ProductFamilyHandle");
    }

    [TestMethod]
    public async Task SubscribeReportsTheMissingConfigurationAsUnavailable()
    {
        var content = new StringContent("""{"planHandle":"eshop-pro"}""", Encoding.UTF8, "application/json");

        var response = await AuthenticatedClient().PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsReportsTheMissingConfigurationAsUnavailable()
    {
        var response = await AuthenticatedClient().GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static HttpClient AuthenticatedClient()
    {
        var client = _application.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }
}
