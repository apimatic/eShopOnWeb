using System;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesTheApiAddressFromTheSiteSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void PrefersAnExplicitBaseUrlOverTheSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = "https://gateway.example.com/maxio" };

        Assert.Equal("https://gateway.example.com/maxio/", settings.ResolveBaseAddress().ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TreatsABlankBaseUrlAsNotSupplied(string? baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = baseUrl };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAbsolute()
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = "/maxio" };

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseAddress());
    }
}
