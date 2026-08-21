using Microsoft.eShopWeb;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore;

public class MaxioSettingsTests
{
    [Fact]
    public void GetApiBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://custom.example.test/billing/"
        };

        Assert.Equal("https://custom.example.test/billing", settings.GetApiBaseUrl());
    }

    [Fact]
    public void GetApiBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4" };

        Assert.Equal("https://cp-exp-4.chargify.com", settings.GetApiBaseUrl());
    }

    [Fact]
    public void GetApiBaseUrl_ThrowsWhenNeitherBaseUrlNorSubdomainIsSet()
    {
        var settings = new MaxioSettings();

        Assert.Throws<InvalidOperationException>(() => settings.GetApiBaseUrl());
    }

    [Fact]
    public void DeriveApiBaseUrl_UsesEuHostForEuEnvironment()
    {
        Assert.Equal(
            "https://cp-exp-4.ebilling.maxio.com",
            MaxioSettings.DeriveApiBaseUrl("cp-exp-4", "EU"));
    }

    [Fact]
    public void DeriveApiBaseUrl_UsesChargifyHostForUsEnvironment()
    {
        Assert.Equal(
            "https://cp-exp-4.chargify.com",
            MaxioSettings.DeriveApiBaseUrl("cp-exp-4", "US"));
    }
}
