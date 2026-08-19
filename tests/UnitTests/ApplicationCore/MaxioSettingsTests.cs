using Microsoft.eShopWeb;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore;

public class MaxioSettingsTests
{
    [Fact]
    public void GetApiBaseUrlUsesBaseUrlWhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://billing.example.test/v1/"
        };

        Assert.Equal("https://billing.example.test/v1", settings.GetApiBaseUrl());
    }

    [Fact]
    public void GetApiBaseUrlDerivesChargifyHostFromSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4" };

        Assert.Equal("https://cp-exp-4.chargify.com", settings.GetApiBaseUrl());
    }

    [Fact]
    public void IsConfiguredRequiresApiKeyAndSite()
    {
        Assert.False(new MaxioSettings().IsConfigured);
        Assert.False(new MaxioSettings { ApiKey = "key" }.IsConfigured);
        Assert.True(new MaxioSettings { ApiKey = "key", Subdomain = "site" }.IsConfigured);
        Assert.True(new MaxioSettings { ApiKey = "key", BaseUrl = "https://example.test" }.IsConfigured);
    }
}
