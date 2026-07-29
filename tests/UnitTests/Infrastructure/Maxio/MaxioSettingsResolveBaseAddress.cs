using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsResolveBaseAddress
{
    [Fact]
    public void DerivesFromSubdomainWhenBaseUrlNotSet()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4" };

        Assert.Equal("https://cp-exp-4.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenSet()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-4",
            BaseUrl = "https://gateway.example.com/api/v1/billing"
        };

        // BaseUrl overrides the derived address; a trailing slash is ensured so relative paths resolve.
        Assert.Equal("https://gateway.example.com/api/v1/billing/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void PreservesExistingTrailingSlashOnBaseUrl()
    {
        var settings = new MaxioSettings { Subdomain = "ignored", BaseUrl = "https://custom.example.com/" };

        Assert.Equal("https://custom.example.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void RelativePathResolvesUnderDerivedBase()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4" };

        var resolved = new System.Uri(settings.ResolveBaseAddress(), "subscriptions.json");

        Assert.Equal("https://cp-exp-4.chargify.com/subscriptions.json", resolved.ToString());
    }
}
