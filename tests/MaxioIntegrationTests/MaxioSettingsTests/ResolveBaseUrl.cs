using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioSettingsTests;

/// <summary>
/// The outbound target must be configuration-driven: the same build has to be able to hit
/// production, a dev tenant, or a local mock (plan.md §2.3). These tests pin the resolution order.
/// </summary>
public class ResolveBaseUrl
{
    [Fact]
    public void DerivesTheUsHostFromTheSubdomainWhenNoOverrideIsSet()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", Environment = "US" };

        Assert.Equal("https://apimatic-hackathon.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesTheEuHostWhenTheRegionIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", Environment = "eu" };

        Assert.Equal("https://apimatic-hackathon.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void PrefersAnExplicitBaseUrlOverTheSubdomainDerivedHost()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            Environment = "US",
            BaseUrl = "http://localhost:8080"
        };

        // The override must win verbatim, or pointing a test run at a mock silently leaks live traffic.
        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void UsesAnExplicitBaseUrlVerbatimEvenForADifferentRegion()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            Environment = "EU",
            BaseUrl = "https://staging.example.test/api"
        };

        Assert.Equal("https://staging.example.test/api", settings.ResolveBaseUrl());
    }

    [Fact]
    public void TreatsAWhitespaceOnlyBaseUrlAsAbsent()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", BaseUrl = "   " };

        // An empty value in appsettings.json means "use the derived host", not "call nowhere".
        Assert.Equal("https://apimatic-hackathon.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void TrimsSurroundingWhitespaceFromAnExplicitBaseUrl()
    {
        var settings = new MaxioSettings { Subdomain = "site", BaseUrl = "  https://mock.local:9000  " };

        Assert.Equal("https://mock.local:9000", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ThrowsWhenNeitherABaseUrlNorASubdomainIsConfigured()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("Maxio:BaseUrl", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void ThrowsWhenTheExplicitBaseUrlIsNotAnAbsoluteUrl()
    {
        var settings = new MaxioSettings { Subdomain = "site", BaseUrl = "localhost:8080" };

        // A relative value would otherwise silently fall back to the derived host at request time.
        var exception = Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("localhost:8080", exception.Message);
    }
}
