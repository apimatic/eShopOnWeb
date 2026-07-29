using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_DerivesFromSubdomain_WhenNoOverride()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_UsesOverride_WhenBaseUrlSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = "https://acme.ebilling.maxio.com" };

        Assert.Equal("https://acme.ebilling.maxio.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_NormalizesTrailingSlash_OnOverride()
    {
        var settings = new MaxioSettings { BaseUrl = "https://custom.example.com/" };

        Assert.Equal("https://custom.example.com/", settings.ResolveBaseUrl());
    }
}
