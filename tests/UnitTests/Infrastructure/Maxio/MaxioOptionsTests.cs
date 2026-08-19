using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://custom.example.test/ab"
        };

        Assert.Equal("https://custom.example.test/ab/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };

        Assert.Equal("https://cp-exp-2.chargify.com/", options.ResolveBaseUrl());
    }

    [Fact]
    public void NormalizeBaseUrl_DoesNotDoubleSlash()
    {
        Assert.Equal("https://site.chargify.com/", MaxioOptions.NormalizeBaseUrl("https://site.chargify.com/"));
    }
}
