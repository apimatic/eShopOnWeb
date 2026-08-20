using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://override.example.test/api"
        };

        Assert.Equal("https://override.example.test/api/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };

        Assert.Equal("https://cp-exp-2.chargify.com/", options.ResolveBaseUrl());
    }

    [Fact]
    public void TryResolveBaseUrl_ReturnsNullWhenNeitherBaseUrlNorSubdomainSet()
    {
        var options = new MaxioOptions();

        Assert.Null(options.TryResolveBaseUrl());
    }
}
