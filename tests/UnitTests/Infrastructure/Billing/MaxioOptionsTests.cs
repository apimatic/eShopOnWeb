using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlOverrideVerbatim()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://example.test/maxio"
        };

        var address = options.ResolveBaseAddress();

        Assert.Equal("https://example.test/maxio/", address.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-1" };

        var address = options.ResolveBaseAddress();

        Assert.Equal("https://cp-exp-1.chargify.com/", address.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_ThrowsWhenNeitherBaseUrlNorSubdomainIsSet()
    {
        var options = new MaxioOptions();

        Assert.Throws<MaxioConfigurationException>(() => options.ResolveBaseAddress());
    }
}
