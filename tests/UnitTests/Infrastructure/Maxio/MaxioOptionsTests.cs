using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesOverrideWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://example.test/api/"
        };

        Assert.Equal("https://example.test/api", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };

        Assert.Equal("https://cp-exp-2.chargify.com", options.ResolveBaseUrl());
    }

    [Fact]
    public void IsConfigured_RequiresKeyFamilyAndAddress()
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
            BaseUrl = "https://override.example",
            ProductFamilyHandle = "family"
        }.IsConfigured);
    }
}
