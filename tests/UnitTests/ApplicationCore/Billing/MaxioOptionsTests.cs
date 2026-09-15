using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveApiBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://example.test/ab"
        };

        Assert.Equal("https://example.test/ab/", options.ResolveApiBaseUrl("EU"));
    }

    [Fact]
    public void ResolveApiBaseUrl_DoesNotDoubleSlashWhenBaseUrlAlreadyHasOne()
    {
        var options = new MaxioOptions { BaseUrl = "https://example.test/" };

        Assert.Equal("https://example.test/", options.ResolveApiBaseUrl());
    }

    [Fact]
    public void ResolveApiBaseUrl_UsesUsChargifyHostByDefault()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };

        Assert.Equal("https://cp-exp-2.chargify.com/", options.ResolveApiBaseUrl());
        Assert.Equal("https://cp-exp-2.chargify.com/", options.ResolveApiBaseUrl("sandbox"));
        Assert.Equal("https://cp-exp-2.chargify.com/", options.ResolveApiBaseUrl("US"));
    }

    [Fact]
    public void ResolveApiBaseUrl_UsesEuHostWhenEnvironmentIsEu()
    {
        var options = new MaxioOptions { Subdomain = "acme" };

        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveApiBaseUrl("EU"));
        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveApiBaseUrl("ebilling"));
    }

    [Fact]
    public void ResolveApiBaseUrl_ThrowsWhenNeitherBaseUrlNorSubdomainIsSet()
    {
        var options = new MaxioOptions();

        Assert.Throws<InvalidOperationException>(() => options.ResolveApiBaseUrl());
    }
}
