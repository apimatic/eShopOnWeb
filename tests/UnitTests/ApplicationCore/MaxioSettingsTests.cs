using Microsoft.eShopWeb;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesOverrideWhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "example-subdomain",
            BaseUrl = "https://billing.example.test/v1"
        };

        Assert.Equal("https://billing.example.test/v1", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4" };

        Assert.Equal("https://cp-exp-4.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void IsConfigured_RequiresKeyFamilyAndHost()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "eshop-subscribe"
        };

        Assert.True(settings.IsConfigured());
        settings.ApiKey = "";
        Assert.False(settings.IsConfigured());
    }
}
