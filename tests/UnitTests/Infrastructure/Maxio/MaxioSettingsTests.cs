using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    private static MaxioSettings Settings(string? baseUrl = null, string subdomain = "example-site") => new()
    {
        ApiKey = "not-a-real-key",
        Subdomain = subdomain,
        ProductFamilyHandle = "eshop-subscribe",
        BaseUrl = baseUrl
    };

    [Fact]
    public void DerivesTheApiAddressFromTheSubdomainWhenNoOverrideIsSet()
    {
        Assert.Equal("https://example-site.chargify.com/", Settings().ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesTheConfiguredBaseUrlVerbatimWhenOneIsSet()
    {
        Assert.Equal("https://billing.example.test/api/",
            Settings(baseUrl: "https://billing.example.test/api/").ResolveBaseAddress().ToString());
    }

    [Fact]
    public void AppendsTheTrailingSlashHttpClientNeedsToKeepThePath()
    {
        Assert.Equal("https://billing.example.test/api/",
            Settings(baseUrl: "https://billing.example.test/api").ResolveBaseAddress().ToString());
    }

    [Fact]
    public void RejectsSettingsWithNeitherASubdomainNorABaseUrl()
    {
        var result = new MaxioSettingsValidator().Validate(null, Settings(subdomain: string.Empty));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Maxio:Subdomain"));
    }

    [Fact]
    public void RejectsSettingsWithoutAnApiKey()
    {
        var settings = Settings();
        settings.ApiKey = string.Empty;

        var result = new MaxioSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Maxio:ApiKey"));
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAbsolute()
    {
        var result = new MaxioSettingsValidator().Validate(null, Settings(baseUrl: "not-a-url"));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Maxio:BaseUrl"));
    }

    [Fact]
    public void AcceptsASubdomainOnlyConfiguration()
    {
        Assert.True(new MaxioSettingsValidator().Validate(null, Settings()).Succeeded);
    }
}
