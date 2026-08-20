using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsResolveBaseUrl
{
    [Fact]
    public void UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "should-not-be-used",
            BaseUrl = "https://billing.example.test/v1/"
        };

        Assert.Equal("https://billing.example.test/v1", options.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesChargifyUrlFromSubdomainWhenBaseUrlIsMissing()
    {
        var options = new MaxioOptions { Subdomain = "acme-site" };

        Assert.Equal("https://acme-site.chargify.com", options.ResolveBaseUrl());
    }

    [Fact]
    public void ThrowsWhenNeitherBaseUrlNorSubdomainIsSet()
    {
        var options = new MaxioOptions();

        Assert.Throws<InvalidOperationException>(() => options.ResolveBaseUrl());
    }
}
