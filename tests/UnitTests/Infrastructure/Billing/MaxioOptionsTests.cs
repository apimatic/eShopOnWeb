using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void GetApiBaseAddress_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-1" };

        Assert.Equal("https://cp-exp-1.chargify.com/", options.GetApiBaseAddress().ToString());
    }

    [Fact]
    public void GetApiBaseAddress_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://billing.example.test/api"
        };

        Assert.Equal("https://billing.example.test/api/", options.GetApiBaseAddress().ToString());
    }

    [Fact]
    public void IsConfigured_RequiresKeyFamilyAndHost()
    {
        Assert.False(new MaxioOptions { ApiKey = "k", ProductFamilyHandle = "fam" }.IsConfigured);
        Assert.True(new MaxioOptions { ApiKey = "k", ProductFamilyHandle = "fam", Subdomain = "site" }.IsConfigured);
        Assert.True(new MaxioOptions { ApiKey = "k", ProductFamilyHandle = "fam", BaseUrl = "https://example.test" }.IsConfigured);
    }
}
