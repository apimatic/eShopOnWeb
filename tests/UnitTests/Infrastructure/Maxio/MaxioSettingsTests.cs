using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesTheAddressFromTheSiteSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlVerbatimAndIgnoresTheSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = "https://acme.ebilling.maxio.com" };

        Assert.Equal("https://acme.ebilling.maxio.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ResolveBaseAddress_KeepsAPathOnAnOverriddenBaseUrl()
    {
        var settings = new MaxioSettings { BaseUrl = "https://connector.api.maxio.com/api/v1/billing/" };

        Assert.Equal("https://connector.api.maxio.com/api/v1/billing/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ResolveBaseAddress_ThrowsWhenNeitherSubdomainNorBaseUrlIsSet()
    {
        Assert.Throws<BillingConfigurationException>(() => new MaxioSettings().ResolveBaseAddress());
    }

    [Fact]
    public void Validate_NamesEveryMissingKey()
    {
        var exception = Assert.Throws<BillingConfigurationException>(() => new MaxioSettings().Validate());

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }

    [Fact]
    public void Validate_AcceptsABaseUrlInPlaceOfASubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            BaseUrl = "https://acme.chargify.com",
            ProductFamilyHandle = "family"
        };

        settings.Validate();
    }

    [Fact]
    public void TryResolveBaseAddress_ReportsFailureInsteadOfThrowing()
    {
        Assert.False(new MaxioSettings().TryResolveBaseAddress(out var baseAddress));
        Assert.Null(baseAddress);
    }
}
