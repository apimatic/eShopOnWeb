using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUri_DerivesFromSubdomain_WhenBaseUrlNotSet()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-8", ApiKey = "k", ProductFamilyHandle = "f" };

        Assert.Equal(new Uri("https://cp-exp-8.chargify.com/"), settings.ResolveBaseUri());
    }

    [Fact]
    public void ResolveBaseUri_UsesBaseUrlVerbatim_WhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-8",
            BaseUrl = "https://billing.internal.example.com/v2",
            ApiKey = "k",
            ProductFamilyHandle = "f"
        };

        // Override wins over subdomain-derived URL; a trailing slash is ensured for relative paths.
        Assert.Equal(new Uri("https://billing.internal.example.com/v2/"), settings.ResolveBaseUri());
    }

    [Fact]
    public void ResolveBaseUri_DoesNotDoubleTrailingSlash()
    {
        var settings = new MaxioSettings { BaseUrl = "https://host.example.com/", ApiKey = "k", ProductFamilyHandle = "f" };

        Assert.Equal(new Uri("https://host.example.com/"), settings.ResolveBaseUri());
    }

    [Fact]
    public void Validate_Throws_WhenApiKeyMissing()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-8", ProductFamilyHandle = "f" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenNeitherSubdomainNorBaseUrlSet()
    {
        var settings = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenProductFamilyHandleMissing()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "cp-exp-8" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Passes_WithApiKeyBaseUrlAndFamily()
    {
        var settings = new MaxioSettings { ApiKey = "k", BaseUrl = "https://host/", ProductFamilyHandle = "f" };

        settings.Validate(); // does not throw
    }
}
