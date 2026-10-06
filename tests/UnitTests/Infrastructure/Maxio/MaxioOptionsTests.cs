using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void GetBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "cp-exp-3",
            BaseUrl = "https://custom.example.com/api"
        };

        Assert.Equal("https://custom.example.com/api", options.GetBaseUrl().ToString());
    }

    [Fact]
    public void GetBaseUrl_DerivesChargifyUrlForUsEnvironment()
    {
        var options = new MaxioOptions
        {
            Subdomain = "cp-exp-3",
            Environment = "US"
        };

        Assert.Equal("https://cp-exp-3.chargify.com", options.GetBaseUrl().GetLeftPart(UriPartial.Authority));
    }

    [Fact]
    public void GetBaseUrl_DerivesMaxioUrlForEuEnvironment()
    {
        var options = new MaxioOptions
        {
            Subdomain = "cp-exp-3",
            Environment = "EU"
        };

        Assert.Equal("https://cp-exp-3.ebilling.maxio.com", options.GetBaseUrl().GetLeftPart(UriPartial.Authority));
    }
}
