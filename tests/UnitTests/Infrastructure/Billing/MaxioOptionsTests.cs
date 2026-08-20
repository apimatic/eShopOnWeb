using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void GetApiBaseAddress_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://override.example.test/billing"
        };

        var address = options.GetApiBaseAddress();

        Assert.Equal("https://override.example.test/billing/", address.ToString());
    }

    [Fact]
    public void GetApiBaseAddress_DerivesChargifyOriginFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-1" };

        var address = options.GetApiBaseAddress();

        Assert.Equal("https://cp-exp-1.chargify.com/", address.ToString());
    }

    [Fact]
    public void IsConfigured_RequiresKeyFamilyAndOrigin()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "eshop-subscribe"
        };

        Assert.True(options.IsConfigured);
    }
}
