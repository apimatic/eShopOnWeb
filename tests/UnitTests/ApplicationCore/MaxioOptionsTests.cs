using Microsoft.eShopWeb;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesOverrideWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://example.test/billing"
        };

        Assert.Equal("https://example.test/billing/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };

        Assert.Equal("https://cp-exp-3.chargify.com/", options.ResolveBaseUrl());
    }
}
