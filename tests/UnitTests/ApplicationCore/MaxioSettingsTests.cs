using Microsoft.eShopWeb.ApplicationCore;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore;

public class MaxioSettingsTests
{
    [Fact]
    public void GetBaseUrl_DerivesFromSubdomain_WhenBaseUrlNotSet()
    {
        var settings = new MaxioSettings { Subdomain = "my-site" };

        Assert.Equal("https://my-site.chargify.com", settings.GetBaseUrl());
    }

    [Fact]
    public void GetBaseUrl_UsesOverride_WhenBaseUrlSet()
    {
        var settings = new MaxioSettings { Subdomain = "my-site", BaseUrl = "https://custom.example.com/" };

        Assert.Equal("https://custom.example.com", settings.GetBaseUrl());
    }

    [Fact]
    public void GetBaseUrl_TreatsWhitespaceBaseUrl_AsNotSet()
    {
        var settings = new MaxioSettings { Subdomain = "my-site", BaseUrl = "   " };

        Assert.Equal("https://my-site.chargify.com", settings.GetBaseUrl());
    }
}
