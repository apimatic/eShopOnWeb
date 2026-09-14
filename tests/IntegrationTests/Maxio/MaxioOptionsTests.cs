using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesUsProductionUrlFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-4" };

        var baseAddress = options.ResolveBaseAddress();

        // Matches the spec's US production server template: https://{site}.chargify.com
        Assert.Equal("https://cp-exp-4.chargify.com/", baseAddress.ToString());
    }

    [Fact]
    public void ResolveBaseAddress_UsesBaseUrlOverrideVerbatim()
    {
        var options = new MaxioOptions
        {
            Subdomain = "cp-exp-4",
            BaseUrl = "https://cp-exp-4.ebilling.maxio.com"
        };

        var baseAddress = options.ResolveBaseAddress();

        // Override wins over the derived URL (trailing slash normalized for relative requests).
        Assert.Equal("https://cp-exp-4.ebilling.maxio.com/", baseAddress.ToString());
    }

    [Fact]
    public void Validate_Throws_WhenApiKeyMissing()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-4", ProductFamilyHandle = "eshop-subscribe" };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenNeitherSubdomainNorBaseUrlPresent()
    {
        var options = new MaxioOptions { ApiKey = "key", ProductFamilyHandle = "eshop-subscribe" };

        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_Succeeds_WithApiKeySubdomainAndFamily()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "cp-exp-4",
            ProductFamilyHandle = "eshop-subscribe"
        };

        options.Validate();
    }
}
