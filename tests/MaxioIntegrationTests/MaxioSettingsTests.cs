using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The configurable target server (§2.3): an explicit BaseUrl always wins, otherwise the host is
/// derived from the subdomain and the data-center region. A regression here would silently send
/// live traffic to the wrong tenant, so each rule is asserted on its own.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void ExplicitBaseUrlWinsOverSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", BaseUrl = "http://localhost:8080" };

        Assert.Equal("http://localhost:8080/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlKeepsItsPathPrefix()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", BaseUrl = "http://localhost:8080/maxio" };

        // The trailing slash is what stops relative request URIs from eating the prefix.
        Assert.Equal("http://localhost:8080/maxio/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesUsHostFromSubdomainWhenNoOverrideIsSet()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", Environment = "US" };

        Assert.Equal("https://cp-exp-3.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesEuHostWhenRegionIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", Environment = "EU" };

        Assert.Equal("https://cp-exp-3.ebilling.maxio.com/", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankBaseUrlFallsBackToTheDerivedHost(string? baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", BaseUrl = baseUrl };

        Assert.Equal("https://cp-exp-3.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void RefusesToResolveWhenNeitherBaseUrlNorSubdomainIsConfigured()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("Maxio:BaseUrl", exception.Message);
    }

    [Fact]
    public void UsesRemittanceCollectionByDefaultSoSubscribeNeedsNoCardCapture()
    {
        Assert.Equal("remittance", new MaxioSettings().PaymentCollectionMethod);
    }
}
