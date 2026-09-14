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
/// Covers what the subscription endpoints do without reaching Maxio: who they let in, what they reject, and
/// how they behave on a deployment that has no billing configuration. The Maxio conversation itself is
/// covered by the unit tests, which stub the transport.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private const string PlansRoute = "/api/subscription-plans";
    private const string SubscriptionsRoute = "/api/subscriptions";
    private const string MySubscriptionsRoute = "/api/my-subscriptions";

    [DataTestMethod]
    [DataRow(PlansRoute)]
    [DataRow(MySubscriptionsRoute)]
    public async Task GetRequiresABearerToken(string route)
    {
        var response = await ProgramTest.NewClient.GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingRequiresABearerToken()
    {
        var response = await ProgramTest.NewClient.PostAsync(SubscriptionsRoute, PlanHandle("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingWithoutAPlanHandleIsRejectedBeforeAnyBillingCallIsMade()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync(SubscriptionsRoute, Json("{}"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow(PlansRoute)]
    [DataRow(MySubscriptionsRoute)]
    public async Task WithoutBillingConfigurationTheCapabilityReportsItselfUnavailable(string route)
    {
        using var factory = new UnconfiguredBillingFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync(route);

        // 503, not 500: the shopper did nothing wrong and an operator has something to fix.
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    public async Task WithoutBillingConfigurationTheRestOfTheApiKeepsWorking()
    {
        using var factory = new UnconfiguredBillingFactory();

        var response = await factory.CreateClient().GetAsync("/api/catalog-brands");

        // Subscription billing is additive: losing it must not take the storefront API down with it.
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static StringContent PlanHandle(string planHandle) => Json($$"""{"planHandle":"{{planHandle}}"}""");

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>A host whose <c>Maxio</c> section is deliberately blank, whatever the machine is configured with.</summary>
    private sealed class UnconfiguredBillingFactory : WebApplicationFactory<Program>
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
