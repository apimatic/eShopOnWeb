using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioSettingsTests;

/// <summary>
/// The target server must be switchable between production, a dev/sandbox tenant and a local
/// mock purely through configuration, with an explicit override always winning.
/// </summary>
public class ResolveBaseUrl
{
    [Fact]
    public void DerivesTheUsHostFromTheSubdomainWhenNoOverrideIsSet()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1", Environment = "US" };

        Assert.Equal("https://cp-exp-1.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesTheEuropeanHostWhenTheRegionIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1", Environment = "eu" };

        Assert.Equal("https://cp-exp-1.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void TreatsAnUnrecognisedRegionAsUs()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1", Environment = null };

        Assert.Equal("https://cp-exp-1.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void AnExplicitBaseUrlWinsOverTheSubdomainDerivedHost()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-1",
            Environment = "US",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void AnExplicitBaseUrlWinsEvenWhenTheRegionIsEuropean()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-1",
            Environment = "EU",
            BaseUrl = "https://staging.example.com"
        };

        Assert.Equal("https://staging.example.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnEmptyBaseUrlFallsBackToTheDerivedHost(string? baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1", BaseUrl = baseUrl };

        Assert.Equal("https://cp-exp-1.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ThrowsAConfigurationErrorWhenNeitherBaseUrlNorSubdomainIsConfigured()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());

        Assert.Contains("BaseUrl", exception.Message);
        Assert.Contains("Subdomain", exception.Message);
    }
}
