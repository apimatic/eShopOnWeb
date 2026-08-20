using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioBaseUrlTests
{
    [Fact]
    public void UsesBaseUrlOverrideWhenSet()
    {
        var options = new MaxioOptions
        {
            BaseUrl = "https://example.test/chargify",
            Subdomain = "ignored"
        };

        var uri = MaxioBaseUrl.Resolve(options, "EU");

        Assert.Equal("https://example.test/chargify/", uri.ToString());
    }

    [Fact]
    public void DerivesUsHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };

        var uri = MaxioBaseUrl.Resolve(options);

        Assert.Equal("https://cp-exp-2.chargify.com/", uri.ToString());
    }

    [Fact]
    public void DerivesEuHostWhenEnvironmentIsEu()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };

        var uri = MaxioBaseUrl.Resolve(options, "EU");

        Assert.Equal("https://cp-exp-2.ebilling.maxio.com/", uri.ToString());
    }

    [Fact]
    public void FallsBackToPlaceholderWhenUnconfigured()
    {
        var uri = MaxioBaseUrl.Resolve(new MaxioOptions());

        Assert.Equal(MaxioBaseUrl.DefaultPlaceholder, uri.ToString());
    }
}
