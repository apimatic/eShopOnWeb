using Microsoft.eShopWeb.ApplicationCore.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void GetApiBaseUri_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };

        Assert.Equal("https://cp-exp-3.chargify.com/", options.GetApiBaseUri().ToString());
    }

    [Fact]
    public void GetApiBaseUri_UsesBaseUrlOverrideVerbatim()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://example.api.maxio.com/api/v1/billing"
        };

        Assert.Equal("https://example.api.maxio.com/api/v1/billing/", options.GetApiBaseUri().ToString());
    }

    [Fact]
    public void EnsureConfigured_ThrowsWhenApiKeyMissing()
    {
        var options = new MaxioOptions
        {
            Subdomain = "cp-exp-3",
            ProductFamilyHandle = "eshop-subscribe"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.EnsureConfigured());
        Assert.Contains("MAXIO_API_KEY", ex.Message);
    }
}
