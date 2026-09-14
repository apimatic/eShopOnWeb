using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_DerivesFromSubdomain_WhenBaseUrlNotSet()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("https://custom.example.com/api", "https://custom.example.com/api")]
    [InlineData("https://custom.example.com/api/", "https://custom.example.com/api")]
    public void ResolveBaseUrl_UsesBaseUrlVerbatim_WhenSet(string baseUrl, string expected)
    {
        var settings = new MaxioSettings { Subdomain = "ignored", BaseUrl = baseUrl };

        Assert.Equal(expected, settings.ResolveBaseUrl());
    }
}
