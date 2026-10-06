using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests;

/// <summary>
/// HTTP-surface tests for the subscription endpoints. Auth enforcement and request
/// validation run without touching Maxio; fake credentials (never network-valid) are injected
/// so the no-network paths stay deterministic regardless of machine secrets.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Maxio:ApiKey"] = "invalid-test-key",
                    ["Maxio:Subdomain"] = "invalid-test-site",
                    ["Maxio:ProductFamilyHandle"] = "eshop-subscribe",
                });
            });
        });

    [TestMethod]
    public async Task GetSubscriptionPlansWithoutTokenReturns401()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PostSubscriptionWithoutTokenReturns401()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var content = new StringContent(
            JsonSerializer.Serialize(new { planHandle = "eshop-pro" }),
            Encoding.UTF8,
            "application/json");
        var response = await client.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetMySubscriptionsWithoutTokenReturns401()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PostSubscriptionWithoutPlanHandleReturns400BeforeAnyProviderCall()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var content = new StringContent(
            JsonSerializer.Serialize(new { planHandle = "" }),
            Encoding.UTF8,
            "application/json");
        var response = await client.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "plan handle");
    }
}
