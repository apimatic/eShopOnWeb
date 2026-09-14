using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesFromSubdomain_WhenNoBaseUrl()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().AbsoluteUri);
    }

    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlVerbatim_WhenSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = "https://acme.ebilling.maxio.com" };

        Assert.Equal("https://acme.ebilling.maxio.com/", settings.ResolveBaseAddress().AbsoluteUri);
    }

    [Fact]
    public void ResolveBaseAddress_Throws_WhenNeitherSet()
    {
        var settings = new MaxioSettings();

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseAddress());
    }
}
