using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void TryGetBaseAddress_UsesSubdomainChargifyHost()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };

        Assert.True(options.TryGetBaseAddress(out var address));
        Assert.Equal("https://cp-exp-3.chargify.com/", address.ToString());
    }

    [Fact]
    public void TryGetBaseAddress_PrefersExplicitBaseUrl()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored",
            BaseUrl = "https://example.test/billing"
        };

        Assert.True(options.TryGetBaseAddress(out var address));
        Assert.Equal("https://example.test/billing/", address.ToString());
    }

    [Fact]
    public void IsConfigured_RequiresApiKeySubdomainAndFamily()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "site",
            ProductFamilyHandle = "family"
        };

        Assert.True(options.IsConfigured);

        options.ApiKey = "";
        Assert.False(options.IsConfigured);
    }
}
