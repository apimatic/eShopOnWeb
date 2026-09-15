using Microsoft.eShopWeb;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.MaxioOptionsTests;

public class ResolveBaseUrl
{
    [Fact]
    public void UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored",
            BaseUrl = "https://override.example.test/billing"
        };

        Assert.Equal("https://override.example.test/billing/", options.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesChargifyHostFromSubdomainWhenBaseUrlIsEmpty()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-1" };

        Assert.Equal("https://cp-exp-1.chargify.com/", options.ResolveBaseUrl());
    }
}
