using System;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseAddressUsesBaseUrlWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored",
            BaseUrl = "https://example.test/api"
        };

        Assert.Equal(new Uri("https://example.test/api/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddressDerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };

        Assert.Equal(new Uri("https://cp-exp-3.chargify.com/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddressRequiresSubdomainWhenBaseUrlMissing()
    {
        var options = new MaxioOptions();
        Assert.Throws<InvalidOperationException>(() => options.ResolveBaseAddress());
    }
}
