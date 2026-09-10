using System;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_DerivesFromSubdomain_WhenNoOverride()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal(new Uri("https://acme.chargify.com/"), settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_UsesOverrideVerbatim_WhenSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = "https://proxy.internal/maxio" };

        Assert.Equal(new Uri("https://proxy.internal/maxio/"), settings.ResolveBaseUrl());
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
    public void Validate_Throws_WhenProductFamilyMissing()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "acme" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Passes_WithBaseUrlInsteadOfSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "key", BaseUrl = "https://acme.chargify.com", ProductFamilyHandle = "fam" };

        settings.Validate(); // should not throw
    }
}
