using System;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlVerbatim_WhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored",
            BaseUrl = "https://billing.example.test/v1"
        };

        var address = options.ResolveBaseAddress("EU");

        Assert.Equal("https://billing.example.test/v1/", address.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_UsesChargifyHost_ForUsEnvironment()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-4" };

        var address = options.ResolveBaseAddress("US");

        Assert.Equal("https://cp-exp-4.chargify.com/", address.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_UsesMaxioEuHost_ForEuEnvironment()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-4" };

        var address = options.ResolveBaseAddress("EU");

        Assert.Equal("https://cp-exp-4.ebilling.maxio.com/", address.ToString());
    }
}
