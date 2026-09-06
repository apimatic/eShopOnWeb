using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesUsHostFromSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "example-site", Environment = "US" };

        Assert.Equal("https://example-site.chargify.com/", settings.ResolveBaseAddress()!.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_DerivesEuHostFromSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "example-site", Environment = "eu" };

        Assert.Equal("https://example-site.ebilling.maxio.com/", settings.ResolveBaseAddress()!.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_DefaultsToUsWhenEnvironmentIsUnrecognised()
    {
        var settings = new MaxioSettings { Subdomain = "example-site", Environment = "somewhere-else" };

        Assert.Equal("https://example-site.chargify.com/", settings.ResolveBaseAddress()!.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlVerbatimWhenSupplied()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored-subdomain",
            Environment = "US",
            BaseUrl = "https://billing.internal.example.com/maxio/"
        };

        Assert.Equal("https://billing.internal.example.com/maxio/", settings.ResolveBaseAddress()!.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_KeepsBaseUrlPathByAppendingASlash()
    {
        // Without a trailing slash HttpClient would drop "/maxio" when resolving relative paths.
        var settings = new MaxioSettings { BaseUrl = "https://billing.internal.example.com/maxio" };

        Assert.Equal("https://billing.internal.example.com/maxio/", settings.ResolveBaseAddress()!.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_IsNullWhenNeitherBaseUrlNorSubdomainIsSet()
    {
        Assert.Null(new MaxioSettings().ResolveBaseAddress());
    }

    [Fact]
    public void GetMissingSettings_NamesEveryUnsetKey()
    {
        var missing = new MaxioSettings().GetMissingSettings();

        Assert.Contains(missing, key => key.Contains("Maxio:ApiKey"));
        Assert.Contains(missing, key => key.Contains("Maxio:Subdomain"));
        Assert.Contains(missing, key => key.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void GetMissingSettings_AcceptsBaseUrlInPlaceOfSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "not-a-real-key",
            ProductFamilyHandle = "demo-family",
            BaseUrl = "https://billing.internal.example.com"
        };

        Assert.Empty(settings.GetMissingSettings());
        Assert.True(settings.IsConfigured);
    }

    [Fact]
    public void IsConfigured_IsFalseWhenTheApiKeyIsBlank()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "   ",
            Subdomain = "example-site",
            ProductFamilyHandle = "demo-family"
        };

        Assert.False(settings.IsConfigured);
    }
}
