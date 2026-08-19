using Microsoft.eShopWeb;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore;

public class MaxioSettingsTests
{
    [Fact]
    public void GetApiBaseAddress_UsesBaseUrlOverrideWhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored",
            BaseUrl = "https://custom.example.com/api"
        };

        var uri = settings.GetApiBaseAddress("EU");

        Assert.Equal("https://custom.example.com/api/", uri.ToString());
    }

    [Fact]
    public void GetApiBaseAddress_UsesUsChargifyHostByDefault()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2" };

        var uri = settings.GetApiBaseAddress();

        Assert.Equal("https://cp-exp-2.chargify.com/", uri.ToString());
    }

    [Fact]
    public void GetApiBaseAddress_UsesEuHostWhenEnvironmentIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2" };

        var uri = settings.GetApiBaseAddress("EU");

        Assert.Equal("https://cp-exp-2.ebilling.maxio.com/", uri.ToString());
    }

    [Fact]
    public void IsConfigured_RequiresApiKeyAndHost()
    {
        Assert.False(new MaxioSettings { ApiKey = "k" }.IsConfigured);
        Assert.True(new MaxioSettings { ApiKey = "k", Subdomain = "site" }.IsConfigured);
        Assert.True(new MaxioSettings { ApiKey = "k", BaseUrl = "https://example.com" }.IsConfigured);
    }
}
