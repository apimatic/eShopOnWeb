using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveApiBaseAddress_UsesBaseUrlWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://custom.example.com/billing"
        };

        Assert.Equal("https://custom.example.com/billing/", options.ResolveApiBaseAddress().ToString());
    }

    [Fact]
    public void ResolveApiBaseAddress_DerivesChargifyUrlFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };

        Assert.Equal("https://cp-exp-2.chargify.com/", options.ResolveApiBaseAddress().ToString());
    }
}
