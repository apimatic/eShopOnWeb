using Microsoft.eShopWeb;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore;

public class MaxioSettingsTests
{
    [Fact]
    public void GetApiBaseUrl_UsesBaseUrlWhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored",
            BaseUrl = "https://override.example.com"
        };

        Assert.Equal("https://override.example.com/", settings.GetApiBaseUrl());
    }

    [Fact]
    public void GetApiBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", settings.GetApiBaseUrl());
    }
}
