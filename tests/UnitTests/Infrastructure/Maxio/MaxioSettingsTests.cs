using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUri_DerivesFromSubdomain_WhenBaseUrlNotSet()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "cp-exp-7", ProductFamilyHandle = "f" };

        Assert.Equal("https://cp-exp-7.chargify.com/", settings.ResolveBaseUri().AbsoluteUri);
    }

    [Fact]
    public void ResolveBaseUri_UsesBaseUrlVerbatim_WhenSet()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "cp-exp-7",
            ProductFamilyHandle = "f",
            BaseUrl = "https://billing.internal.example.com",
        };

        Assert.Equal("https://billing.internal.example.com/", settings.ResolveBaseUri().AbsoluteUri);
    }

    [Fact]
    public void ResolveBaseUri_Throws_WhenNeitherSubdomainNorBaseUrlSet()
    {
        var settings = new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f" };

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUri());
    }

    [Fact]
    public void IsConfigured_RequiresApiKeyFamilyAndAddress()
    {
        Assert.False(new MaxioSettings().IsConfigured);
        Assert.False(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f" }.IsConfigured); // no address
        Assert.True(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", Subdomain = "s" }.IsConfigured);
        Assert.True(new MaxioSettings { ApiKey = "k", ProductFamilyHandle = "f", BaseUrl = "https://x" }.IsConfigured);
    }
}
