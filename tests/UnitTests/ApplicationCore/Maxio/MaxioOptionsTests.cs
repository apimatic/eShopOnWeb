using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_DerivesFromSubdomain_WhenBaseUrlNotSet()
    {
        var options = new MaxioOptions { ApiKey = "key", Subdomain = "cp-exp-4" };

        Assert.Equal("https://cp-exp-4.chargify.com/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_UsesOverride_WhenBaseUrlSet()
    {
        var options = new MaxioOptions { ApiKey = "key", Subdomain = "cp-exp-4", BaseUrl = "https://custom.example.com" };

        Assert.Equal("https://custom.example.com/", options.ResolveBaseUrl());
    }

    [Fact]
    public void IsConfigured_FalseWithoutApiKey()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-4" };

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void IsConfigured_TrueWithApiKeyAndSubdomain()
    {
        var options = new MaxioOptions { ApiKey = "key", Subdomain = "cp-exp-4" };

        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void IsConfigured_TrueWithApiKeyAndBaseUrlOnly()
    {
        var options = new MaxioOptions { ApiKey = "key", BaseUrl = "https://custom.example.com" };

        Assert.True(options.IsConfigured);
    }
}
