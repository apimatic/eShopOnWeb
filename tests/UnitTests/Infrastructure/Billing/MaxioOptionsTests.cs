using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesBaseUrlWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-subdomain",
            BaseUrl = "https://example.test/maxio"
        };

        Assert.Equal("https://example.test/maxio/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };

        Assert.Equal("https://cp-exp-3.chargify.com/", options.ResolveBaseUrl());
    }
}
