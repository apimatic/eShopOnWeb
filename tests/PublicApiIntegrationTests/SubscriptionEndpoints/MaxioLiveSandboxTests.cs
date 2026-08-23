using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioLiveSandboxTests
{
    [TestMethod]
    [TestCategory("LiveMaxio")]
    public async Task HeroFlowAgainstSandboxIsAuthenticatedAndIdempotent()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_MAXIO_LIVE_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive("Set RUN_MAXIO_LIVE_TESTS=true to run the Maxio sandbox test.");
        }

        var apiKey = RequiredEnvironmentVariable("MAXIO_API_KEY");
        var subdomain = RequiredEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
        var familyHandle = RequiredEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY");
        Assert.AreEqual("US", RequiredEnvironmentVariable("MAXIO_ENVIRONMENT"), true, "This integration is configured for the SDK's US hosting region.");

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOptions<MaxioOptions>>();
                services.RemoveAll<MaxioAdvancedBillingClient>();
                services.RemoveAll<ISubscriptionBillingService>();

                services.AddSingleton(Options.Create(new MaxioOptions
                {
                    ApiKey = apiKey,
                    Subdomain = subdomain,
                    ProductFamilyHandle = familyHandle
                }));
                services.AddSingleton(_ => CreateClient(apiKey, subdomain));
                services.AddScoped<ISubscriptionBillingService, SubscriptionBillingService>();
            }));

        using var client = factory.CreateClient();
        var sdkClient = factory.Services.GetRequiredService<MaxioAdvancedBillingClient>();
        var site = await sdkClient.Sites.ReadSite(ct: default);
        Assert.AreEqual(true, site.Site.Test, "Configured Maxio site must report itself as a test site.");

        var authentication = await client.PostAsJsonAsync("/api/authenticate", new AuthenticateRequest
        {
            Username = "demouser@microsoft.com",
            Password = AuthorizationConstants.DEFAULT_PASSWORD
        });
        authentication.EnsureSuccessStatusCode();
        var auth = await authentication.Content.ReadFromJsonAsync<AuthenticateResponse>();
        Assert.IsNotNull(auth?.Token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var plansHttp = await client.GetAsync("/api/subscription-plans");
        plansHttp.EnsureSuccessStatusCode();
        var plans = await plansHttp.Content.ReadFromJsonAsync<SubscriptionPlansResponse>();
        Assert.IsNotNull(plans);
        Assert.IsTrue(plans.Plans.Count >= 2);
        var target = plans.Plans.FirstOrDefault(x => x.Handle == "eshop-pro") ?? plans.Plans[0];

        var firstHttp = await client.PostAsJsonAsync("/api/subscriptions", new SubscribeRequest { ProductHandle = target.Handle });
        var firstBody = await firstHttp.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Created, firstHttp.StatusCode, firstBody);
        var first = await firstHttp.Content.ReadFromJsonAsync<SubscribeResponse>();
        Assert.IsNotNull(first);

        var replayHttp = await client.PostAsJsonAsync("/api/subscriptions", new SubscribeRequest { ProductHandle = target.Handle });
        Assert.AreEqual(HttpStatusCode.OK, replayHttp.StatusCode);
        var replay = await replayHttp.Content.ReadFromJsonAsync<SubscribeResponse>();
        Assert.IsNotNull(replay);
        Assert.AreEqual(first.Subscription.Id, replay.Subscription.Id);
        Assert.AreEqual(first.Subscription.Reference, replay.Subscription.Reference);
        Assert.AreEqual(target.Handle, replay.Subscription.ProductHandle);
        Assert.AreEqual(target.PriceInCents, replay.Subscription.PriceInCents);
        Assert.IsNotNull(replay.Subscription.NextBillingDate);

        var mineHttp = await client.GetAsync("/api/my-subscriptions");
        mineHttp.EnsureSuccessStatusCode();
        var mine = await mineHttp.Content.ReadFromJsonAsync<MySubscriptionsResponse>();
        Assert.IsNotNull(mine);
        Assert.IsTrue(mine.Subscriptions.Any(x => x.Id == first.Subscription.Id));
    }

    private static MaxioAdvancedBillingClient CreateClient(string apiKey, string subdomain)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(10)
            }
        };
        options.Server.Production.Us.Site = subdomain;
        var guard = new MaxioWriteOnceHandler
        {
            InnerHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            }
        };
        return new MaxioAdvancedBillingClient(new HttpClient(guard) { Timeout = TimeSpan.FromSeconds(10) }, options);
    }

    private static string RequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new AssertFailedException($"Required environment variable {name} is missing.");
}
