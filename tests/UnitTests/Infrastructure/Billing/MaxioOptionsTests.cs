using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://custom.example.com/v1/"
        };

        Assert.Equal("https://custom.example.com/v1", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-4" };

        Assert.Equal("https://cp-exp-4.chargify.com", options.ResolveBaseUrl());
    }

    [Fact]
    public void IsConfigured_RequiresKeyFamilyAndAddress()
    {
        Assert.False(new MaxioOptions().IsConfigured);
        Assert.True(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "eshop-subscribe"
        }.IsConfigured);
        Assert.True(new MaxioOptions
        {
            ApiKey = "key",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://example.test"
        }.IsConfigured);
    }
}
