using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    private static MaxioSettings Valid() => new()
    {
        ApiKey = "not-a-real-key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    [Fact]
    public void ResolveBaseAddress_DerivesTheAddressFromTheSubdomain()
    {
        Assert.Equal("https://acme.chargify.com/", Valid().ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ResolveBaseAddress_PrefersAnExplicitBaseUrl()
    {
        var settings = Valid();
        settings.BaseUrl = "https://gateway.example.com/api/v1/billing/";

        Assert.Equal("https://gateway.example.com/api/v1/billing/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ResolveBaseAddress_KeepsAnExplicitBaseUrlPathWhenItHasNoTrailingSlash()
    {
        var settings = Valid();
        settings.BaseUrl = "https://gateway.example.com/api/v1/billing";

        // Relative request paths would otherwise resolve against the parent of the last segment and
        // silently drop it.
        Assert.Equal("https://gateway.example.com/api/v1/billing/", settings.ResolveBaseAddress().ToString());
        Assert.Equal("https://gateway.example.com/api/v1/billing/customers.json",
            new Uri(settings.ResolveBaseAddress(), "customers.json").ToString());
    }

    [Fact]
    public void ResolveBaseAddress_IgnoresAnEmptyBaseUrl()
    {
        var settings = Valid();
        settings.BaseUrl = "   ";

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void Validate_AcceptsACompleteConfiguration()
    {
        Assert.True(new MaxioSettingsValidator().Validate(null, Valid()).Succeeded);
    }

    [Fact]
    public void Validate_RejectsAMissingApiKey()
    {
        var settings = Valid();
        settings.ApiKey = string.Empty;

        var result = new MaxioSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Maxio:ApiKey", result.FailureMessage);
    }

    [Fact]
    public void Validate_RejectsAMissingProductFamilyHandle()
    {
        var settings = Valid();
        settings.ProductFamilyHandle = string.Empty;

        var result = new MaxioSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Maxio:ProductFamilyHandle", result.FailureMessage);
    }

    [Fact]
    public void Validate_RejectsAMissingSubdomainWhenThereIsNoBaseUrl()
    {
        var settings = Valid();
        settings.Subdomain = string.Empty;

        var result = new MaxioSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Maxio:Subdomain", result.FailureMessage);
    }

    [Fact]
    public void Validate_AllowsAMissingSubdomainWhenABaseUrlIsGiven()
    {
        var settings = Valid();
        settings.Subdomain = string.Empty;
        settings.BaseUrl = "https://gateway.example.com/";

        Assert.True(new MaxioSettingsValidator().Validate(null, settings).Succeeded);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://gateway.example.com/")]
    public void Validate_RejectsAnUnusableBaseUrl(string baseUrl)
    {
        var settings = Valid();
        settings.BaseUrl = baseUrl;

        Assert.True(new MaxioSettingsValidator().Validate(null, settings).Failed);
    }
}
