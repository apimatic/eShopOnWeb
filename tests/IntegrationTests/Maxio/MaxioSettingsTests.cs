using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Unit-level tests for <see cref="MaxioSettings"/>, covering base-URL derivation, the optional
/// explicit override, and required-settings validation.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesFromSubdomain_WhenNoBaseUrlProvided()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        var baseAddress = settings.ResolveBaseAddress();

        Assert.Equal("https://acme.chargify.com/", baseAddress.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlVerbatim_WhenProvided()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "acme",
            BaseUrl = "https://proxy.internal.example.com/maxio"
        };

        var baseAddress = settings.ResolveBaseAddress();

        // The override is used instead of deriving from the subdomain; a trailing slash is ensured
        // so relative request paths compose correctly.
        Assert.Equal("https://proxy.internal.example.com/maxio/", baseAddress.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_PreservesExistingTrailingSlash()
    {
        var settings = new MaxioSettings { BaseUrl = "https://proxy.example.com/" };

        Assert.Equal("https://proxy.example.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void Validate_Throws_WhenApiKeyMissing()
    {
        var settings = new MaxioSettings { Subdomain = "acme", ProductFamilyHandle = "fam" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenSubdomainAndBaseUrlMissing()
    {
        var settings = new MaxioSettings { ApiKey = "key", ProductFamilyHandle = "fam" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Passes_WithBaseUrlButNoSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            ProductFamilyHandle = "fam",
            BaseUrl = "https://proxy.example.com/"
        };

        // Should not throw: an explicit BaseUrl substitutes for the subdomain.
        settings.Validate();
    }

    [Fact]
    public void Validate_Throws_WhenProductFamilyMissing()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "acme" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }
}
