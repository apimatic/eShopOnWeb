using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsResolveBaseAddress
{
    [Fact]
    public void DerivesChargifyUrlFromSubdomainWhenNoOverride()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        var result = settings.ResolveBaseAddress();

        Assert.Equal("https://acme.chargify.com/", result.ToString());
    }

    [Fact]
    public void UsesBaseUrlOverrideVerbatimWhenSet()
    {
        var settings = new MaxioSettings { Subdomain = "ignored", BaseUrl = "https://proxy.internal/maxio" };

        var result = settings.ResolveBaseAddress();

        Assert.Equal("https://proxy.internal/maxio/", result.ToString());
    }

    [Fact]
    public void AppendsTrailingSlashSoRelativePathsCompose()
    {
        var settings = new MaxioSettings { BaseUrl = "https://acme.chargify.com" };

        var baseAddress = settings.ResolveBaseAddress();
        var composed = new Uri(baseAddress, "customers.json");

        Assert.Equal("https://acme.chargify.com/customers.json", composed.ToString());
    }
}
