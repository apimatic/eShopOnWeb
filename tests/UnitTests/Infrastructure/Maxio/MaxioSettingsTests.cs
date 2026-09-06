using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesTheBaseAddressFromTheSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "acme" };

        Assert.Equal(new Uri("https://acme.chargify.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void ExplicitBaseUrlWinsOverTheSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "acme",
            BaseUrl = "https://billing.internal.example.com/v1/"
        };

        Assert.Equal(new Uri("https://billing.internal.example.com/v1/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void ExplicitBaseUrlIsUsedVerbatimApartFromTrailingSlashNormalisation()
    {
        // Relative request URIs only compose correctly against a base address ending in a slash.
        var settings = new MaxioSettings { ApiKey = "key", BaseUrl = "https://billing.example.com/api" };

        Assert.Equal(new Uri("https://billing.example.com/api/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void IsConfiguredRequiresAKeyAndAnAddress()
    {
        Assert.False(new MaxioSettings().IsConfigured);
        Assert.False(new MaxioSettings { Subdomain = "acme" }.IsConfigured);
        Assert.False(new MaxioSettings { ApiKey = "key" }.IsConfigured);
        Assert.True(new MaxioSettings { ApiKey = "key", Subdomain = "acme" }.IsConfigured);
        Assert.True(new MaxioSettings { ApiKey = "key", BaseUrl = "https://billing.example.com/" }.IsConfigured);
    }

    [Fact]
    public void RejectsAMalformedBaseUrl()
    {
        var settings = new MaxioSettings { ApiKey = "key", BaseUrl = "not a url" };

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseAddress());
    }

    [Fact]
    public void ShipsNoCatalogOrCredentialDefaults()
    {
        // The same build has to run against a different site and catalog, so nothing about the
        // sandbox may be baked into the defaults.
        var settings = new MaxioSettings();

        Assert.Null(settings.ApiKey);
        Assert.Null(settings.Subdomain);
        Assert.Null(settings.BaseUrl);
        Assert.Null(settings.ProductFamilyHandle);
    }
}
