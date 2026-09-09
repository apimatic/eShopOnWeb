using Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Subscriptions;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_DerivesFromSubdomain_WhenNoOverride()
    {
        var settings = new MaxioSettings { Subdomain = "test-site" };

        Assert.Equal("https://test-site.chargify.com/", settings.ResolveBaseUrl().ToString());
    }

    [Fact]
    public void ResolveBaseUrl_UsesOverrideVerbatim_WhenBaseUrlSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored",
            BaseUrl = "https://custom.example.com/api"
        };

        Assert.Equal("https://custom.example.com/api/", settings.ResolveBaseUrl().ToString());
    }

    [Fact]
    public void ResolveBaseUrl_EnsuresTrailingSlash()
    {
        var settings = new MaxioSettings { BaseUrl = "https://custom.example.com" };

        Assert.EndsWith("/", settings.ResolveBaseUrl().ToString());
    }
}
