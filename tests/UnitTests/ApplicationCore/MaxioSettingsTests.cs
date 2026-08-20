using Microsoft.eShopWeb;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveApiBaseAddress_UsesBaseUrlWhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://custom.example.test/api/"
        };

        Assert.Equal("https://custom.example.test/api", settings.ResolveApiBaseAddress());
    }

    [Fact]
    public void ResolveApiBaseAddress_DerivesChargifyHostFromSubdomain()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-2"
        };

        Assert.Equal("https://cp-exp-2.chargify.com", settings.ResolveApiBaseAddress());
    }

    [Fact]
    public void IsConfigured_RequiresKeyFamilyAndHost()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            ProductFamilyHandle = "eshop-subscribe",
            Subdomain = "cp-exp-2"
        };

        Assert.True(settings.IsConfigured);
    }

    [Fact]
    public void IsConfigured_AllowsBaseUrlInsteadOfSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            ProductFamilyHandle = "family",
            BaseUrl = "https://example.test"
        };

        Assert.True(settings.IsConfigured);
    }
}
