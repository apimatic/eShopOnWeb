using System;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesFromSubdomain_WhenNoBaseUrl()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "cp-exp-7", ProductFamilyHandle = "eshop-subscribe" };

        Assert.Equal(new Uri("https://cp-exp-7.chargify.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlVerbatim_WhenProvided()
    {
        // When Maxio:BaseUrl is set it must be used verbatim, overriding subdomain derivation.
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "cp-exp-7",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://billing.example.com/api",
        };

        Assert.Equal(new Uri("https://billing.example.com/api/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddress_KeepsExistingTrailingSlash()
    {
        var settings = new MaxioSettings { ApiKey = "k", BaseUrl = "https://billing.example.com/", ProductFamilyHandle = "f" };

        Assert.Equal(new Uri("https://billing.example.com/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void BuildBasicAuthParameter_EncodesApiKeyWithLiteralXPassword()
    {
        var settings = new MaxioSettings { ApiKey = "secret-key", Subdomain = "s", ProductFamilyHandle = "f" };

        var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes("secret-key:x"));
        Assert.Equal(expected, settings.BuildBasicAuthParameter());
    }

    [Theory]
    [InlineData("k", "s", "f", null, true)]      // subdomain + family + key
    [InlineData("k", null, "f", "https://b/", true)] // base url substitutes for subdomain
    [InlineData(null, "s", "f", null, false)]    // missing api key
    [InlineData("k", null, "f", null, false)]    // no subdomain and no base url
    [InlineData("k", "s", null, null, false)]    // missing product family handle
    public void IsConfigured_ReflectsRequiredSettings(string? apiKey, string? subdomain, string? family, string? baseUrl, bool expected)
    {
        var settings = new MaxioSettings
        {
            ApiKey = apiKey,
            Subdomain = subdomain,
            ProductFamilyHandle = family,
            BaseUrl = baseUrl,
        };

        Assert.Equal(expected, settings.IsConfigured);
    }

    [Fact]
    public void EnsureConfigured_Throws_WhenNotConfigured()
    {
        var settings = new MaxioSettings();

        Assert.Throws<BillingNotConfiguredException>(() => settings.EnsureConfigured());
    }
}
