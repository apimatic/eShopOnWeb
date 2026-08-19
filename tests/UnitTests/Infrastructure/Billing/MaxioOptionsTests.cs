using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrlUsesOverrideWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://example.test/maxio/"
        };

        Assert.Equal("https://example.test/maxio", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrlDerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };

        Assert.Equal("https://cp-exp-2.chargify.com", options.ResolveBaseUrl());
    }
}
