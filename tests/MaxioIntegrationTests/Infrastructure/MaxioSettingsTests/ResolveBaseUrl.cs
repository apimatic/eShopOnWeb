using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.MaxioSettingsTests;

/// <summary>
/// The outbound target server must be switchable between production, a dev/sandbox tenant and a
/// local mock purely through configuration (plan.md §2.3).
/// </summary>
public class ResolveBaseUrl
{
    [Fact]
    public void DerivesTheUsHostFromTheSubdomainWhenNoOverrideIsSet()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2", Environment = "US" };

        Assert.Equal("https://cp-exp-2.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesTheEuHostForTheEuRegion()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2", Environment = "EU" };

        Assert.Equal("https://cp-exp-2.ebilling.maxio.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DefaultsToTheUsHostForAnUnrecognisedRegion()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2", Environment = "somewhere-else" };

        Assert.Equal("https://cp-exp-2.chargify.com/", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    [InlineData("http://localhost:8080/", "http://localhost:8080/")]
    [InlineData("https://sandbox.example.com/maxio/", "https://sandbox.example.com/maxio/")]
    public void AnExplicitBaseUrlWinsOverTheDerivedHost(string configured, string expected)
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-2",
            Environment = "US",
            BaseUrl = configured
        };

        // The same build must be retargetable at a mock or a dev tenant without a code change.
        Assert.Equal(expected, settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyBaseUrlFallsBackToTheDerivedHost(string? configured)
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-2",
            Environment = "US",
            BaseUrl = configured
        };

        Assert.Equal("https://cp-exp-2.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void FailsClearlyWhenNeitherASubdomainNorABaseUrlIsConfigured()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());

        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public async Task TheClientActuallySendsItsTrafficToTheConfiguredTargetServer()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondWith(HttpMethod.Get, "product_families.json", HttpStatusCode.OK, "[]");

        var settings = new MaxioSettings
        {
            ApiKey = "test-api-key",
            Subdomain = "cp-exp-2",
            Environment = "US",
            BaseUrl = "http://localhost:8080",
            ProductFamilyHandle = "eshop-subscribe"
        };

        // No BaseAddress is preset here, so the client must resolve the target from configuration.
        var client = new MaxioBillingClient(new HttpClient(handler), Options.Create(settings));

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.ListPlansAsync());

        Assert.Equal("http://localhost:8080/product_families.json", RequestedAbsoluteUri(handler));
    }

    [Fact]
    public async Task TheClientTargetsTheDerivedHostWhenNoOverrideIsConfigured()
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondWith(HttpMethod.Get, "product_families.json", HttpStatusCode.OK, "[]");

        var settings = new MaxioSettings
        {
            ApiKey = "test-api-key",
            Subdomain = "cp-exp-2",
            Environment = "US",
            ProductFamilyHandle = "eshop-subscribe"
        };

        var client = new MaxioBillingClient(new HttpClient(handler), Options.Create(settings));

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.ListPlansAsync());

        Assert.Equal("https://cp-exp-2.chargify.com/product_families.json", RequestedAbsoluteUri(handler));
    }

    private static string RequestedAbsoluteUri(StubHttpMessageHandler handler) =>
        handler.AbsoluteUris.Single();
}
