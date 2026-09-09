using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUri_DerivesFromSubdomain_WhenNoBaseUrlOverride()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-7" };

        var uri = settings.ResolveBaseUri();

        Assert.Equal("https://cp-exp-7.chargify.com/", uri.ToString());
    }

    [Fact]
    public void ResolveBaseUri_UsesBaseUrlVerbatim_WhenOverrideSet()
    {
        // The override must win over the subdomain-derived URL (a different site/catalog).
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-7",
            BaseUrl = "https://proxy.internal.example.com/maxio"
        };

        var uri = settings.ResolveBaseUri();

        Assert.Equal("https://proxy.internal.example.com/maxio/", uri.ToString());
    }

    [Fact]
    public void ResolveBaseUri_DoesNotDoubleTrailingSlash()
    {
        var settings = new MaxioSettings { BaseUrl = "https://acme.chargify.com/" };

        var uri = settings.ResolveBaseUri();

        Assert.Equal("https://acme.chargify.com/", uri.ToString());
    }

    [Fact]
    public void Validate_Throws_WhenApiKeyMissing()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-7", ProductFamilyHandle = "eshop-subscribe" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenNeitherSubdomainNorBaseUrlProvided()
    {
        var settings = new MaxioSettings { ApiKey = "key", ProductFamilyHandle = "eshop-subscribe" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Passes_WithBaseUrlOverrideAndNoSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            BaseUrl = "https://acme.chargify.com",
            ProductFamilyHandle = "eshop-subscribe"
        };

        settings.Validate(); // should not throw
    }
}
