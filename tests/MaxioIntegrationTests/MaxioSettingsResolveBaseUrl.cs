using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The outbound target server is a configuration knob, not a code path: the same build must be
/// able to hit production, a dev/sandbox tenant, or a local mock. These tests pin the resolution
/// order that makes that true.
/// </summary>
public class MaxioSettingsResolveBaseUrl
{
    [Fact]
    public void DerivesUsHostFromSubdomainWhenNoExplicitBaseUrlIsSet()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", Environment = "US" };

        Assert.Equal("https://cp-exp-3.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesEuHostWhenRegionIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", Environment = "EU" };

        Assert.Equal("https://cp-exp-3.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlWinsOverTheSubdomainDerivedHost()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-3",
            Environment = "US",
            BaseUrl = "https://some-other-tenant.chargify.com"
        };

        Assert.Equal("https://some-other-tenant.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlMayPointAtALocalMockServer()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-3",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlWinsEvenInTheEuRegion()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-3",
            Environment = "EU",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void AnEmptyBaseUrlFallsBackToTheDerivedHostRatherThanBeingUsedVerbatim()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", BaseUrl = "   " };

        Assert.Equal("https://cp-exp-3.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void TrailingSlashesAreTrimmedSoPathsAreNotDoubled()
    {
        var settings = new MaxioSettings { BaseUrl = "https://cp-exp-3.chargify.com/" };

        Assert.Equal("https://cp-exp-3.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ThrowsWhenNeitherABaseUrlNorASubdomainIsConfigured()
    {
        var settings = new MaxioSettings();

        var ex = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("Maxio:BaseUrl", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://cp-exp-3.chargify.com")]
    [InlineData("/relative/path")]
    public void ThrowsWhenTheExplicitBaseUrlIsNotAnAbsoluteHttpUrl(string configured)
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", BaseUrl = configured };

        var ex = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("absolute http or https URL", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExposesOnlyDurableHandlesToTheDomain()
    {
        var settings = new MaxioSettings
        {
            ProductFamilyHandle = "eshop-subscribe",
            DefaultProductHandle = "eshop-pro",
            AlternateProductHandle = "basic-plan",
            MeteredComponentHandle = "api-call"
        };

        var catalog = (Microsoft.eShopWeb.ApplicationCore.Interfaces.ISubscriptionCatalogSettings)settings;

        Assert.Equal("eshop-subscribe", catalog.ProductFamilyHandle);
        Assert.Equal("eshop-pro", catalog.DefaultPlanHandle);
        Assert.Equal("basic-plan", catalog.AlternatePlanHandle);
        Assert.Equal("api-call", catalog.MeteredComponentHandle);
    }
}
