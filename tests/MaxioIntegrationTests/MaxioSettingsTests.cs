using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The target server must be configuration-driven: the same build has to reach production, a
/// sandbox tenant or a local mock without a code change (plan §2.3).
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void ExplicitBaseUrlWinsOverTheSubdomainDerivedHost()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", BaseUrl = "http://localhost:8080" };

        Assert.Equal("http://localhost:8080/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlKeepsItsPath()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", BaseUrl = "http://localhost:8080/maxio" };

        Assert.Equal("http://localhost:8080/maxio/", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnAbsentBaseUrlFallsBackToTheSubdomainDerivedHost(string? baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", BaseUrl = baseUrl };

        Assert.Equal("https://apimatic-hackathon.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void TheEuRegionDerivesTheEuHost()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", Environment = "eu" };

        Assert.Equal("https://apimatic-hackathon.ebilling.maxio.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void AnUnknownRegionFallsBackToTheUsHost()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", Environment = "APAC" };

        Assert.Equal("https://apimatic-hackathon.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void WithNoTargetAtAllResolutionFailsLoudly()
    {
        var settings = new MaxioSettings();

        Assert.False(settings.TryResolveBaseUrl(out _));
        Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
    }

    [Fact]
    public void AMalformedBaseUrlIsRejectedRatherThanUsed()
    {
        var settings = new MaxioSettings { BaseUrl = "not a url" };

        Assert.False(settings.TryResolveBaseUrl(out _));
    }

    [Fact]
    public void TheCatalogExposesTheConfiguredHandlesToApplicationCore()
    {
        var settings = new MaxioSettings
        {
            ProductFamilyHandle = "eshop-subscribe",
            DefaultProductHandle = "eshop-pro",
            AlternateProductHandle = "basic-plan",
            MeteredComponentHandle = "api-call"
        };

        var catalog = settings.ToCatalog();

        Assert.Equal("eshop-subscribe", catalog.ProductFamilyHandle);
        Assert.Equal("eshop-pro", catalog.DefaultPlanHandle);
        Assert.Equal("basic-plan", catalog.AlternatePlanHandle);
        Assert.Equal("api-call", catalog.MeteredComponentHandle);
    }

    [Fact]
    public void UnsetHandlesSurfaceAsEmptyRatherThanNull()
    {
        var catalog = new MaxioSettings().ToCatalog();

        Assert.Equal(string.Empty, catalog.DefaultPlanHandle);
        Assert.Equal(string.Empty, catalog.MeteredComponentHandle);
    }
}
