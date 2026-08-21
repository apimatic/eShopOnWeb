using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesOverrideVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://example.test/ab/"
        };

        Assert.Equal("https://example.test/ab/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };

        Assert.Equal("https://cp-exp-2.chargify.com/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_TrimsTrailingSlashOnOverride()
    {
        var options = new MaxioOptions { BaseUrl = "https://billing.example.test" };

        Assert.Equal("https://billing.example.test/", options.ResolveBaseUrl());
    }
}
