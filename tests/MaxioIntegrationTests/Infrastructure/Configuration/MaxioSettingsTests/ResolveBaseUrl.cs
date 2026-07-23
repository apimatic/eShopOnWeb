using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.Configuration.MaxioSettingsTests;

public class ResolveBaseUrl
{
    [Fact]
    public void DerivesTheUsHostFromTheSubdomainWhenNoOverrideIsSet()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", Environment = MaxioSettings.US_ENVIRONMENT };

        Assert.Equal("https://apimatic-hackathon.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesTheEuHostWhenTheRegionIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", Environment = MaxioSettings.EU_ENVIRONMENT };

        Assert.Equal("https://apimatic-hackathon.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void PrefersAnExplicitBaseUrlOverTheSubdomainDerivedHost()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            Environment = MaxioSettings.US_ENVIRONMENT,
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void PrefersAnExplicitBaseUrlEvenWhenTheRegionIsEu()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            Environment = MaxioSettings.EU_ENVIRONMENT,
            BaseUrl = "https://billing.example.test"
        };

        Assert.Equal("https://billing.example.test", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FallsBackToTheDerivedHostWhenTheOverrideIsBlank(string? baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", BaseUrl = baseUrl };

        Assert.Equal("https://apimatic-hackathon.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ThrowsWhenNeitherAnOverrideNorASubdomainIsConfigured()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());

        Assert.Contains("Maxio:BaseUrl", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void ProducesABaseAddressThatKeepsAPathOnTheOverride()
    {
        // A mock server mounted under a path must not have that path truncated by relative requests.
        var settings = new MaxioSettings { BaseUrl = "http://localhost:8080/maxio" };

        var baseAddress = MaxioBillingClient.CreateBaseAddress(settings);

        Assert.Equal("http://localhost:8080/maxio/", baseAddress.ToString());
    }
}
