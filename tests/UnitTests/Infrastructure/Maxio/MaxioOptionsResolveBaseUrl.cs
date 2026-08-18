using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsResolveBaseUrl
{
    [Fact]
    public void UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://override.example.com/ab/"
        };

        Assert.Equal("https://override.example.com/ab", options.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesChargifyUrlFromSubdomainWhenBaseUrlMissing()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-1" };

        Assert.Equal("https://cp-exp-1.chargify.com", options.ResolveBaseUrl());
    }

    [Fact]
    public void IsConfiguredRequiresKeyFamilyAndHost()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            ProductFamilyHandle = "eshop-subscribe",
            Subdomain = "cp-exp-1"
        };

        Assert.True(options.IsConfigured);
        Assert.False(new MaxioOptions { ApiKey = "key" }.IsConfigured);
    }
}
