using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioSettingsTests;

public class ResolveBaseUrl
{
    [Fact]
    public void PrefersAnExplicitBaseUrlOverTheSubdomainDerivedHost()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "cp-exp-3",
            BaseUrl = "http://localhost:8080"
        };

        // The whole point of the override: the same build can be pointed anywhere without a recompile.
        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void UsesTheExplicitBaseUrlVerbatimWithoutAppendingTheSubdomain()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key",
            Subdomain = "cp-exp-3",
            BaseUrl = "https://billing.internal.example.com/gateway"
        };

        Assert.Equal("https://billing.internal.example.com/gateway", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesTheUnitedStatesHostFromTheSubdomainWhenNoOverrideIsSet()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "cp-exp-3", Environment = "US" };

        Assert.Equal("https://cp-exp-3.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesTheEuropeanHostWhenTheRegionIsEu()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "cp-exp-3", Environment = "eu" };

        Assert.Equal("https://cp-exp-3.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void TreatsAnEmptyBaseUrlAsNoOverride()
    {
        var settings = new MaxioSettings { ApiKey = "key", Subdomain = "cp-exp-3", BaseUrl = "   " };

        Assert.Equal("https://cp-exp-3.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ThrowsWhenNeitherABaseUrlNorASubdomainIsConfigured()
    {
        var settings = new MaxioSettings { ApiKey = "key" };

        Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
    }

    [Fact]
    public void ThrowsWhenTheConfiguredBaseUrlIsNotAnAbsoluteHttpUrl()
    {
        var settings = new MaxioSettings { ApiKey = "key", BaseUrl = "not-a-url" };

        Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
    }
}
