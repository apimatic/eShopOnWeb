using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void GetApiBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://proxy.example.test/maxio"
        };

        Assert.Equal("https://proxy.example.test/maxio/", options.GetApiBaseUrl());
    }

    [Fact]
    public void GetApiBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", options.GetApiBaseUrl());
    }

    [Fact]
    public void IsConfigured_RequiresKeyFamilyAndTarget()
    {
        Assert.False(new MaxioOptions { ApiKey = "k", ProductFamilyHandle = "fam" }.IsConfigured);
        Assert.True(new MaxioOptions { ApiKey = "k", ProductFamilyHandle = "fam", Subdomain = "site" }.IsConfigured);
        Assert.True(new MaxioOptions { ApiKey = "k", ProductFamilyHandle = "fam", BaseUrl = "https://example.test" }.IsConfigured);
    }
}
