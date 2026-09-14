using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.MaxioTests;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesFromSubdomain_WhenNoOverride()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal(new Uri("https://acme.chargify.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlOverride_WhenSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = "https://acme.maxio.test/api" };

        // Override is used verbatim (with a guaranteed trailing slash for relative resolution).
        Assert.Equal(new Uri("https://acme.maxio.test/api/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddress_Throws_WhenNeitherConfigured()
    {
        var settings = new MaxioSettings();

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseAddress());
    }
}
