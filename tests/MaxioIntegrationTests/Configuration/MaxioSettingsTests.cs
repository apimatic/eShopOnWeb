using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Configuration;

/// <summary>
/// The configurable target server (§2.3): an explicit Maxio:BaseUrl must win verbatim over the
/// subdomain-derived host, so the same build can be pointed at production, a dev tenant, or a mock.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void DerivesUsHostFromSubdomainWhenNoBaseUrlIsConfigured()
    {
        var settings = new MaxioSettings { Subdomain = "example-site", Environment = "US" };

        Assert.Equal("https://example-site.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesEuHostFromSubdomainWhenRegionIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "example-site", Environment = "eu" };

        Assert.True(settings.IsEuRegion);
        Assert.Equal("https://example-site.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("https://dev-tenant.chargify.com")]
    public void ExplicitBaseUrlWinsOverTheDerivedHost(string baseUrl)
    {
        var settings = new MaxioSettings
        {
            Subdomain = "example-site",
            Environment = "US",
            BaseUrl = baseUrl
        };

        Assert.Equal(baseUrl, settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlWinsForEuRegionToo()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "example-site",
            Environment = "EU",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlIsTrimmedOfSurroundingWhitespaceAndTrailingSlash()
    {
        var settings = new MaxioSettings { BaseUrl = "  http://localhost:8080/  " };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void BlankBaseUrlFallsBackToTheDerivedHostRatherThanFailing()
    {
        var settings = new MaxioSettings { Subdomain = "example-site", BaseUrl = "   " };

        Assert.Equal("https://example-site.chargify.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("/relative/path")]
    public void RejectsABaseUrlThatIsNotAnAbsoluteHttpUrl(string baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "example-site", BaseUrl = baseUrl };

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("Maxio:BaseUrl", exception.Message);
    }

    [Fact]
    public void FailsWhenNeitherABaseUrlNorASubdomainIsConfigured()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }
}
