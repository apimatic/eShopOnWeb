using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesBaseUrlOverrideVerbatim_WhenSet()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "other-site",
            Environment = "US",
            BaseUrl = "https://maxio.example.com/api"
        };

        var resolved = options.ResolveBaseUrl();

        Assert.Equal("https://maxio.example.com/api", resolved);
    }

    [Fact]
    public void ResolveBaseUrl_TrimsTrailingSlashFromOverride()
    {
        var options = new MaxioOptions { BaseUrl = "https://maxio.example.com/" };

        Assert.Equal("https://maxio.example.com", options.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("US", "https://cp-exp-3.chargify.com")]
    [InlineData("EU", "https://cp-exp-3.ebilling.maxio.com")]
    public void ResolveBaseUrl_DerivesFromEnvironmentAndSubdomain_WhenNoOverride(string environment, string expected)
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3", Environment = environment };

        Assert.Equal(expected, options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DefaultsToUS()
    {
        var options = new MaxioOptions { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_ThrowsWhenSubdomainMissingAndNoOverride()
    {
        var options = new MaxioOptions();

        Assert.Throws<InvalidOperationException>(() => options.ResolveBaseUrl());
    }
}