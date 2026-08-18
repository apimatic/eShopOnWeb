using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void UsesBaseUrlOverrideWhenSet()
    {
        var options = new MaxioOptions
        {
            BaseUrl = "https://example.test/maxio/",
            Subdomain = "ignored-site"
        };

        Assert.Equal("https://example.test/maxio", options.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesChargifyUrlFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com", options.ResolveBaseUrl());
    }

    [Fact]
    public void ReturnsNullWhenNeitherBaseUrlNorSubdomainIsSet()
    {
        var options = new MaxioOptions();

        Assert.Null(options.TryResolveBaseUrl());
    }
}
