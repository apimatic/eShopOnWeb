using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PublicApiIntegrationTests.Maxio;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory(StubHandler handler)
    {
        var factory = new WebApplicationFactory<Program>();
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "test-api-key",
                ["Maxio:Subdomain"] = "cp-exp-5",
                ["Maxio:ProductFamilyHandle"] = "eshop-subscribe"
            }));

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<MaxioAdvancedBillingClient>();
                services.AddSingleton<MaxioAdvancedBillingClient>(_ => MaxioTestClientFactory.CreateClient(handler));
            });
        });
    }

    private static HttpClient CreateHttpsClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    [TestMethod]
    public async Task SubscriptionPlans_RequiresAuthentication()
    {
        using var factory = CreateFactory(new StubHandler(_ => StubResponses.Ok("[]")));
        var client = CreateHttpsClient(factory);

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscription_RequiresAuthentication()
    {
        using var factory = CreateFactory(new StubHandler(_ => StubResponses.Ok("{}")));
        var client = CreateHttpsClient(factory);

        var response = await client.PostAsync("api/subscriptions",
            new StringContent("""{ "planHandle": "eshop-pro" }""", Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptions_RequiresAuthentication()
    {
        using var factory = CreateFactory(new StubHandler(_ => StubResponses.Ok("[]")));
        var client = CreateHttpsClient(factory);

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
