using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsResolveBaseAddress
{
    [Fact]
    public void UsesBaseUrlWhenSet()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "ignored",
            ProductFamilyHandle = "family",
            BaseUrl = "https://override.example.com/v1"
        };

        var address = options.ResolveBaseAddress();

        Assert.Equal("https://override.example.com/v1/", address.ToString());
    }

    [Fact]
    public void DerivesChargifyUrlFromSubdomainWhenBaseUrlIsEmpty()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "cp-exp-2",
            ProductFamilyHandle = "family"
        };

        var address = options.ResolveBaseAddress();

        Assert.Equal("https://cp-exp-2.chargify.com/", address.ToString());
    }

    [Fact]
    public void IsConfiguredRequiresApiKeyFamilyAndHost()
    {
        var missingHost = new MaxioOptions { ApiKey = "k", ProductFamilyHandle = "fam" };
        var ready = new MaxioOptions { ApiKey = "k", Subdomain = "site", ProductFamilyHandle = "fam" };

        Assert.False(missingHost.IsConfigured);
        Assert.True(ready.IsConfigured);
    }
}
