using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesFromSubdomain_WhenBaseUrlNotSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme-sandbox" };

        var address = settings.ResolveBaseAddress();

        Assert.Equal("https://acme-sandbox.chargify.com/", address.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlVerbatim_WhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored",
            BaseUrl = "https://custom.example.com/api",
        };

        var address = settings.ResolveBaseAddress();

        Assert.Equal("https://custom.example.com/api/", address.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_AppendsTrailingSlash_WhenMissing()
    {
        var settings = new MaxioSettings { BaseUrl = "https://custom.example.com" };

        Assert.EndsWith("/", settings.ResolveBaseAddress().ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ResolveBaseAddress_FallsBackToSubdomain_WhenBaseUrlBlank(string? baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "site", BaseUrl = baseUrl };

        Assert.Equal("https://site.chargify.com/", settings.ResolveBaseAddress().ToString());
    }
}
