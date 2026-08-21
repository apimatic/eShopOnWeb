using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void GetApiBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://billing.example.test/v1/"
        };

        Assert.Equal("https://billing.example.test/v1/", options.GetApiBaseUrl());
    }

    [Fact]
    public void GetApiBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };

        Assert.Equal("https://cp-exp-3.chargify.com/", options.GetApiBaseUrl());
    }

    [Fact]
    public void GetProductFamilyId_PrefixesHandle()
    {
        var options = new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" };

        Assert.Equal("handle:eshop-subscribe", options.GetProductFamilyId());
    }

    [Fact]
    public void GetProductFamilyId_DoesNotDoublePrefix()
    {
        var options = new MaxioOptions { ProductFamilyHandle = "handle:eshop-subscribe" };

        Assert.Equal("handle:eshop-subscribe", options.GetProductFamilyId());
    }

    [Fact]
    public void IsConfigured_RequiresKeyFamilyAndHost()
    {
        Assert.False(new MaxioOptions().IsConfigured());
        Assert.False(new MaxioOptions { ApiKey = "k", ProductFamilyHandle = "fam" }.IsConfigured());
        Assert.True(new MaxioOptions
        {
            ApiKey = "k",
            ProductFamilyHandle = "fam",
            Subdomain = "site"
        }.IsConfigured());
        Assert.True(new MaxioOptions
        {
            ApiKey = "k",
            ProductFamilyHandle = "fam",
            BaseUrl = "https://example.test"
        }.IsConfigured());
    }
}
