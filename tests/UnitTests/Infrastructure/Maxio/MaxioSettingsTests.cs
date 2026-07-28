using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_DerivesFromSubdomain_WhenBaseUrlNotSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_UsesBaseUrlOverride_WhenSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = "https://custom.example.com" };

        Assert.Equal("https://custom.example.com/", settings.ResolveBaseUrl());
    }
}
