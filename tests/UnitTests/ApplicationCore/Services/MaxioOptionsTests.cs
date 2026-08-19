using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://billing.example.test/api/v1"
        };

        var address = options.ResolveBaseAddress();

        Assert.Equal("https://billing.example.test/api/v1/", address.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };

        var address = options.ResolveBaseAddress();

        Assert.Equal("https://cp-exp-3.chargify.com/", address.ToString());
    }
}
