using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioOptionsTests
{
    [Fact]
    public void TryResolveBaseUrl_UsesBaseUrlWhenSet()
    {
        var options = new MaxioOptions
        {
            BaseUrl = "https://billing.example.test/",
            Subdomain = "ignored"
        };

        Assert.Equal("https://billing.example.test", options.TryResolveBaseUrl());
    }

    [Fact]
    public void TryResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-4" };

        Assert.Equal("https://cp-exp-4.chargify.com", options.TryResolveBaseUrl());
    }

    [Fact]
    public void TryResolveBaseUrl_ReturnsNullWhenNeitherBaseUrlNorSubdomainSet()
    {
        var options = new MaxioOptions();

        Assert.Null(options.TryResolveBaseUrl());
    }
}
