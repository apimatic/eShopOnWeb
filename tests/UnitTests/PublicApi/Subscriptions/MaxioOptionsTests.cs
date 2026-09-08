using System;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Subscriptions;

public class MaxioOptionsTests
{
    [Fact]
    public void BaseUrlOverrideIsUsedVerbatim()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "some-site",
            BaseUrl = "https://maxio.internal.example/api-gateway/"
        };

        Assert.Equal("https://maxio.internal.example/api-gateway", options.ResolveBaseUrl());
    }

    [Fact]
    public void BaseUrlIsDerivedFromSubdomainWhenOverrideMissing()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "cp-exp-6",
            BaseUrl = null
        };

        Assert.Equal("https://cp-exp-6.chargify.com", options.ResolveBaseUrl());
    }

    [Fact]
    public void ValidateThrowsWhenApiKeyMissing()
    {
        var options = new MaxioOptions { Subdomain = "site", ProductFamilyHandle = "family", ApiKey = "" };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }
}
