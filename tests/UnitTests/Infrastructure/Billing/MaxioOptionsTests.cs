using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveApiBaseUrl_UsesSpecServerTemplate_WhenBaseUrlIsEmpty()
    {
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "acme",
            ProductFamilyHandle = "plans",
            BaseUrl = ""
        };

        Assert.Equal("https://acme.chargify.com/", options.ResolveApiBaseUrl());
    }

    [Fact]
    public void ResolveApiBaseUrl_UsesBaseUrlVerbatim_WhenSet()
    {
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "ignored",
            ProductFamilyHandle = "plans",
            BaseUrl = "https://override.example.com/v1/"
        };

        Assert.Equal("https://override.example.com/v1/", options.ResolveApiBaseUrl());
    }

    [Fact]
    public void IsConfigured_RequiresKeyFamilyAndSite()
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
            ProductFamilyHandle = "family",
            BaseUrl = "https://example.com"
        }.IsConfigured);
    }
}
