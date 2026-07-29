using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    private static MaxioSettings ValidSettings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe",
    };

    [Fact]
    public void ResolveBaseUri_DerivesFromSubdomain_WhenBaseUrlNotSet()
    {
        var settings = ValidSettings();

        Assert.Equal("https://acme.chargify.com", settings.ResolveBaseUri().AbsoluteUri.TrimEnd('/'));
    }

    [Fact]
    public void ResolveBaseUri_UsesBaseUrlVerbatim_WhenSet()
    {
        var settings = ValidSettings();
        settings.BaseUrl = "https://billing.internal.example.com/v1/";

        Assert.Equal("https://billing.internal.example.com/v1", settings.ResolveBaseUri().AbsoluteUri.TrimEnd('/'));
    }

    [Fact]
    public void ResolveBaseUri_Throws_WhenApiKeyMissing()
    {
        var settings = ValidSettings();
        settings.ApiKey = null;

        Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUri());
    }

    [Fact]
    public void ResolveBaseUri_Throws_WhenProductFamilyHandleMissing()
    {
        var settings = ValidSettings();
        settings.ProductFamilyHandle = " ";

        Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUri());
    }

    [Fact]
    public void ResolveBaseUri_Throws_WhenNeitherBaseUrlNorSubdomainSet()
    {
        var settings = ValidSettings();
        settings.Subdomain = null;
        settings.BaseUrl = null;

        Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUri());
    }
}
