using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://example.test/maxio"
        };

        Assert.Equal("https://example.test/maxio/", options.ResolveApiBaseAddress());
    }

    [Fact]
    public void DerivesChargifyHostFromSubdomainWhenBaseUrlIsMissing()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };

        Assert.Equal("https://cp-exp-3.chargify.com/", options.ResolveApiBaseAddress());
    }
}
