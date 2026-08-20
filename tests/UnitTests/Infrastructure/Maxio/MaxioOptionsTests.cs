using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void GetBaseAddressUsesOverrideWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://billing.example.test/v1"
        };

        Assert.Equal(new Uri("https://billing.example.test/v1/"), options.GetBaseAddress());
    }

    [Fact]
    public void GetBaseAddressDerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };

        Assert.Equal(new Uri("https://cp-exp-2.chargify.com/"), options.GetBaseAddress());
    }

    [Fact]
    public void IsConfiguredRequiresApiKeySubdomainAndFamily()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "eshop-subscribe"
        };

        Assert.True(options.IsConfigured);
        options.ApiKey = "";
        Assert.False(options.IsConfigured);
    }
}
