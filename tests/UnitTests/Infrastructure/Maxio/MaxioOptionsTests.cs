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
            Subdomain = "ignored-subdomain",
            BaseUrl = "https://example.test/ab"
        };

        Assert.Equal("https://example.test/ab/", options.GetApiBaseAddress().ToString());
    }

    [Fact]
    public void DerivesChargifyHostFromSubdomainWhenBaseUrlIsMissing()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };

        Assert.Equal("https://cp-exp-3.chargify.com/", options.GetApiBaseAddress().ToString());
    }
}
