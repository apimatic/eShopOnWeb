using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesOverrideVerbatim()
    {
        var options = new MaxioOptions
        {
            BaseUrl = "https://custom.example.test/ab"
        };

        Assert.Equal("https://custom.example.test/ab/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_UsesUsHostFromOpenApiCatalog()
    {
        var options = new MaxioOptions
        {
            Subdomain = "cp-exp-2",
            Environment = "US"
        };

        Assert.Equal("https://cp-exp-2.chargify.com/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_UsesEuHostWhenEnvironmentIsEu()
    {
        var options = new MaxioOptions
        {
            Subdomain = "acme",
            Environment = "EU"
        };

        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveBaseUrl());
    }

    [Fact]
    public void EnsureConfigured_ThrowsWhenRequiredSettingsMissing()
    {
        var options = new MaxioOptions();

        Assert.Throws<BillingConfigurationException>(() => options.EnsureConfigured());
    }

    [Fact]
    public void IsConfigured_TrueWhenBaseUrlSuppliedWithoutSubdomain()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://example.test"
        };

        Assert.True(options.IsConfigured);
    }
}
