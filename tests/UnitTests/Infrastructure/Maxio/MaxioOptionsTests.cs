using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesOverrideWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "example-site",
            BaseUrl = "https://billing.example.test/"
        };

        Assert.Equal("https://billing.example.test", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "acme-site" };

        Assert.Equal("https://acme-site.chargify.com", options.ResolveBaseUrl());
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
