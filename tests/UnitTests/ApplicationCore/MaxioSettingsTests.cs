using Microsoft.eShopWeb;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored-subdomain",
            BaseUrl = "https://custom.example.test/billing"
        };

        Assert.Equal("https://custom.example.test/billing/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1" };

        Assert.Equal("https://cp-exp-1.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void IsConfigured_RequiresKeyFamilyAndHost()
    {
        Assert.False(new MaxioSettings().IsConfigured);
        Assert.False(new MaxioSettings { ApiKey = "k", Subdomain = "s" }.IsConfigured);
        Assert.True(new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "s",
            ProductFamilyHandle = "eshop-subscribe"
        }.IsConfigured);
        Assert.True(new MaxioSettings
        {
            ApiKey = "k",
            BaseUrl = "https://example.test",
            ProductFamilyHandle = "family"
        }.IsConfigured);
    }
}
