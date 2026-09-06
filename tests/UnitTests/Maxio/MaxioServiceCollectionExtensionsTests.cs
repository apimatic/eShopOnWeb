using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioServiceCollectionExtensionsTests
{
    [Fact]
    public async Task WithoutABaseUrl_TheAddressIsDerivedFromTheConfiguredSubdomain()
    {
        var handler = new StubHttpMessageHandler().WithCatalog();
        var service = BuildFromConfiguration(handler, new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-key",
            ["Maxio:Subdomain"] = "some-site",
            ["Maxio:ProductFamilyHandle"] = MaxioTestContext.FamilyHandle
        });

        await service.GetPlansAsync();

        Assert.Equal("https://some-site.chargify.com", handler.Requests[0].Uri.GetLeftPart(UriPartial.Authority));
    }

    [Fact]
    public async Task WhenABaseUrlIsConfigured_ItIsUsedVerbatimAndTheSubdomainIsIgnored()
    {
        var handler = new StubHttpMessageHandler().WithCatalog();
        var service = BuildFromConfiguration(handler, new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-key",
            ["Maxio:Subdomain"] = "ignored-subdomain",
            ["Maxio:BaseUrl"] = "https://billing-gateway.internal.example.com",
            ["Maxio:ProductFamilyHandle"] = MaxioTestContext.FamilyHandle
        });

        await service.GetPlansAsync();

        Assert.Equal("https://billing-gateway.internal.example.com",
            handler.Requests[0].Uri.GetLeftPart(UriPartial.Authority));
        Assert.DoesNotContain(handler.Requests, request => request.Uri.Host.Contains("ignored-subdomain"));
    }

    [Fact]
    public async Task AConfiguredCollectionMethodOverridesTheOneDerivedFromTheSite()
    {
        var handler = new StubHttpMessageHandler()
            .WithCatalog(relationshipInvoicing: true)
            .On(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioTestContext.CustomerJson())
            .On(HttpMethod.Get, "/customers/555/subscriptions.json", HttpStatusCode.OK, "[]")
            .On(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created,
                MaxioTestContext.SubscriptionJson());

        var service = BuildFromConfiguration(handler, new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-key",
            ["Maxio:Subdomain"] = "some-site",
            ["Maxio:ProductFamilyHandle"] = MaxioTestContext.FamilyHandle,
            ["Maxio:PaymentCollectionMethod"] = "prepaid"
        });

        await service.SubscribeAsync(new Subscriber(MaxioTestContext.SubscriberEmail),
            MaxioTestContext.ProPlanHandle);

        Assert.Contains("\"payment_collection_method\":\"prepaid\"",
            handler.LastOf(HttpMethod.Post, "/subscriptions.json")!.Body);
    }

    [Theory]
    [InlineData("Maxio:ApiKey")]
    [InlineData("Maxio:Subdomain")]
    [InlineData("Maxio:ProductFamilyHandle")]
    public void AMissingRequiredSettingFailsAtStartupNotOnTheFirstShopper(string missingKey)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-key",
            ["Maxio:Subdomain"] = "some-site",
            ["Maxio:ProductFamilyHandle"] = MaxioTestContext.FamilyHandle
        };
        settings.Remove(missingKey);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddMaxioSubscriptionBilling(configuration));

        Assert.Contains(missingKey, exception.Message);
    }

    private static ISubscriptionBillingService BuildFromConfiguration(StubHttpMessageHandler handler,
        Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);

        // Swap the real transport for the stub, leaving the registered handler pipeline intact.
        services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider().GetRequiredService<ISubscriptionBillingService>();
    }
}
