using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUri_DerivesFromSubdomain_WhenNoBaseUrl()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal(new Uri("https://acme.chargify.com/"), settings.ResolveBaseUri());
    }

    [Fact]
    public void ResolveBaseUri_UsesBaseUrlVerbatim_WhenSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = "https://custom.example.com/api" };

        Assert.Equal(new Uri("https://custom.example.com/api/"), settings.ResolveBaseUri());
    }

    [Fact]
    public void Validate_Throws_WhenApiKeyMissing()
    {
        var settings = new MaxioSettings { Subdomain = "acme", ProductFamilyHandle = "fam" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenNeitherSubdomainNorBaseUrl()
    {
        var settings = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "fam" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_Succeeds_WithApiKeySubdomainAndFamily()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "acme", ProductFamilyHandle = "fam" };

        settings.Validate(); // does not throw
    }
}
