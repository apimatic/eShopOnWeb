using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The target server must be configuration-driven so the same build can hit production, a dev
/// tenant, or a local mock. An explicit BaseUrl always wins over the subdomain-derived host.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_DerivesTheUsHostFromTheSubdomain_WhenNoOverrideIsConfigured()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2", Environment = "US" };

        Assert.Equal("https://cp-exp-2.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesTheEuHost_WhenTheRegionIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2", Environment = "eu" };

        Assert.Equal("https://cp-exp-2.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DefaultsToTheUsHost_WhenNoRegionIsConfigured()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2" };

        Assert.Equal("https://cp-exp-2.chargify.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("https://acme.chargify.com")]
    public void ResolveBaseUrl_UsesTheExplicitOverrideVerbatim_AndIgnoresTheSubdomain(string baseUrl)
    {
        var settings = new MaxioSettings
        {
            BaseUrl = baseUrl,
            Subdomain = "cp-exp-2",
            Environment = "US"
        };

        Assert.Equal(baseUrl, settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_PrefersTheOverride_EvenInTheEuRegion()
    {
        var settings = new MaxioSettings
        {
            BaseUrl = "http://localhost:8080",
            Subdomain = "cp-exp-2",
            Environment = "EU"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ResolveBaseUrl_FallsBackToTheDerivedHost_WhenTheOverrideIsBlank(string? baseUrl)
    {
        var settings = new MaxioSettings { BaseUrl = baseUrl, Subdomain = "cp-exp-2" };

        Assert.Equal("https://cp-exp-2.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_Throws_WhenNeitherAnOverrideNorASubdomainIsConfigured()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("BaseUrl", exception.Message);
        Assert.Contains("Subdomain", exception.Message);
    }

    [Fact]
    public void ResolveProductFamilyReference_UsesTheHandleForm_WhenNoNumericIdIsConfigured()
    {
        var settings = new MaxioSettings { ProductFamilyHandle = "eshop-subscribe" };

        Assert.Equal("handle:eshop-subscribe", settings.ResolveProductFamilyReference());
    }

    [Fact]
    public void ResolveProductFamilyReference_PrefersTheNumericId_WhenOneIsConfigured()
    {
        var settings = new MaxioSettings { ProductFamilyHandle = "eshop-subscribe", ProductFamilyId = 3026729 };

        Assert.Equal("3026729", settings.ResolveProductFamilyReference());
    }

    [Fact]
    public void ResolveProductFamilyReference_Throws_WhenTheFamilyIsNotConfiguredAtAll()
    {
        var settings = new MaxioSettings();

        Assert.Throws<BillingConfigurationException>(() => settings.ResolveProductFamilyReference());
    }
}
