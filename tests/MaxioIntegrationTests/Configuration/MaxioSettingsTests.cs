using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Configuration;

/// <summary>
/// The outbound target must be configuration-driven: the same build has to be able to hit
/// production, a sandbox tenant, or a local mock server with no code change.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void DerivesTheUsHostFromTheSubdomainWhenNoBaseUrlIsConfigured()
    {
        var settings = new MaxioSettings { Subdomain = "example-site", Environment = "US" };

        Assert.Equal("https://example-site.chargify.com/", settings.ResolveBaseUrl().AbsoluteUri);
    }

    [Fact]
    public void DerivesTheEuropeanHostWhenTheRegionIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "example-site", Environment = "EU" };

        Assert.Equal("https://example-site.ebilling.maxio.com/", settings.ResolveBaseUrl().AbsoluteUri);
    }

    [Theory]
    [InlineData("eu")]
    [InlineData("Eu")]
    [InlineData(" EU ")]
    public void TreatsTheRegionCaseAndWhitespaceInsensitively(string region)
    {
        var settings = new MaxioSettings { Subdomain = "example-site", Environment = region };

        Assert.Equal("https://example-site.ebilling.maxio.com/", settings.ResolveBaseUrl().AbsoluteUri);
    }

    [Fact]
    public void AnExplicitBaseUrlWinsOverTheSubdomainDerivedHost()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "example-site",
            Environment = "US",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080/", settings.ResolveBaseUrl().AbsoluteUri);
    }

    [Fact]
    public void AnExplicitBaseUrlWinsEvenWhenTheRegionSaysEurope()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "example-site",
            Environment = "EU",
            BaseUrl = "https://staging.example.com"
        };

        Assert.Equal("https://staging.example.com/", settings.ResolveBaseUrl().AbsoluteUri);
    }

    [Fact]
    public void PreservesASubPathOnAnExplicitBaseUrl()
    {
        // A mock server mounted under a prefix must keep that prefix, which is why the resolved
        // address always ends in a slash.
        var settings = new MaxioSettings { BaseUrl = "http://localhost:8080/maxio" };

        Assert.Equal("http://localhost:8080/maxio/", settings.ResolveBaseUrl().AbsoluteUri);
    }

    [Fact]
    public void AnEmptyBaseUrlFallsBackToTheDerivedHost()
    {
        var settings = new MaxioSettings { Subdomain = "example-site", BaseUrl = "   " };

        Assert.Equal("https://example-site.chargify.com/", settings.ResolveBaseUrl().AbsoluteUri);
    }

    [Fact]
    public void ThrowsWhenNeitherABaseUrlNorASubdomainIsConfigured()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());

        Assert.Contains("Maxio:BaseUrl", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void ThrowsWhenTheExplicitBaseUrlIsNotAnAbsoluteUrl()
    {
        var settings = new MaxioSettings { Subdomain = "example-site", BaseUrl = "not-a-url" };

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());

        Assert.Contains("not a valid absolute URL", exception.Message);
    }
}
