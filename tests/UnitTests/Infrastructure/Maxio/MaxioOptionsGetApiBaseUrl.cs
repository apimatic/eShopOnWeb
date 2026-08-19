using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsGetApiBaseUrl
{
    [Fact]
    public void UsesBaseUrlOverrideVerbatim()
    {
        var options = new MaxioOptions
        {
            BaseUrl = "https://billing.example.test/v1/"
        };

        Assert.Equal("https://billing.example.test/v1/", options.GetApiBaseUrl());
    }

    [Fact]
    public void DerivesChargifyUrlFromSubdomain()
    {
        var options = new MaxioOptions
        {
            Subdomain = "my-site"
        };

        Assert.Equal("https://my-site.chargify.com/", options.GetApiBaseUrl());
    }

    [Fact]
    public void IsConfiguredRequiresKeyFamilyAndSite()
    {
        Assert.False(new MaxioOptions().IsConfigured);
        Assert.True(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "family"
        }.IsConfigured);
        Assert.True(new MaxioOptions
        {
            ApiKey = "key",
            BaseUrl = "https://example.test",
            ProductFamilyHandle = "family"
        }.IsConfigured);
    }
}
