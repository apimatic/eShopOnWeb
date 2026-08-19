using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesBaseUrlOverrideVerbatim()
    {
        var options = new MaxioOptions
        {
            BaseUrl = "https://billing.example.test/api",
            Subdomain = "ignored-subdomain"
        };

        Assert.Equal("https://billing.example.test/api/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };

        Assert.Equal("https://cp-exp-3.chargify.com/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_ThrowsWhenNeitherBaseUrlNorSubdomainSet()
    {
        var options = new MaxioOptions();

        Assert.Throws<MaxioConfigurationException>(() => options.ResolveBaseUrl());
    }
}
