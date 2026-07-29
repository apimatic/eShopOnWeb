using System;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Configuration;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_DerivesHostFromSubdomain_WhenBaseUrlNotSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme", ApiKey = "k", ProductFamilyHandle = "fam" };

        var uri = settings.ResolveBaseUrl();

        Assert.Equal("https://acme.chargify.com/", uri.AbsoluteUri);
    }

    [Fact]
    public void ResolveBaseUrl_UsesExplicitBaseUrlVerbatim_WhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "acme",
            ApiKey = "k",
            ProductFamilyHandle = "fam",
            BaseUrl = "https://proxy.example.com/maxio"
        };

        var uri = settings.ResolveBaseUrl();

        // Explicit override wins over the subdomain-derived host.
        Assert.Equal("https://proxy.example.com/maxio/", uri.AbsoluteUri);
    }

    [Fact]
    public void ResolveBaseUrl_PreservesTrailingSlash_WhenAlreadyPresent()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            ProductFamilyHandle = "fam",
            BaseUrl = "https://proxy.example.com/"
        };

        var uri = settings.ResolveBaseUrl();

        Assert.Equal("https://proxy.example.com/", uri.AbsoluteUri);
    }

    [Fact]
    public void Validate_Throws_WhenApiKeyMissing()
    {
        var settings = new MaxioSettings { Subdomain = "acme", ProductFamilyHandle = "fam" };

        var ex = Assert.Throws<InvalidOperationException>(() => settings.Validate());
        Assert.Contains("Maxio:ApiKey", ex.Message);
    }

    [Fact]
    public void Validate_Throws_WhenSubdomainAndBaseUrlBothMissing()
    {
        var settings = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "fam" };

        var ex = Assert.Throws<InvalidOperationException>(() => settings.Validate());
        Assert.Contains("Maxio:Subdomain", ex.Message);
    }

    [Fact]
    public void Validate_Passes_WhenBaseUrlProvidedWithoutSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            ProductFamilyHandle = "fam",
            BaseUrl = "https://proxy.example.com/"
        };

        // Should not throw.
        settings.Validate();
    }

    [Fact]
    public void Validate_Throws_WhenProductFamilyHandleMissing()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "acme" };

        var ex = Assert.Throws<InvalidOperationException>(() => settings.Validate());
        Assert.Contains("Maxio:ProductFamilyHandle", ex.Message);
    }
}
