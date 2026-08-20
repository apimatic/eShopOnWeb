using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveApiBaseUrl_UsesBaseUrlWhenSet()
    {
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "ignored-site",
            ProductFamilyHandle = "family",
            BaseUrl = "https://override.example.com/v1/"
        };

        Assert.Equal("https://override.example.com/v1", options.ResolveApiBaseUrl());
    }

    [Fact]
    public void ResolveApiBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-3",
            ProductFamilyHandle = "eshop-subscribe"
        };

        Assert.Equal("https://cp-exp-3.chargify.com", options.ResolveApiBaseUrl());
    }

    [Fact]
    public void EnsureConfigured_RequiresApiKey()
    {
        var options = new MaxioOptions
        {
            Subdomain = "site",
            ProductFamilyHandle = "family"
        };

        Assert.Throws<BillingConfigurationException>(() => options.EnsureConfigured());
    }
}
