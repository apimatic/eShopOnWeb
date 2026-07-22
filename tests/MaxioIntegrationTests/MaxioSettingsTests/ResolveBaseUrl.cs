using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioSettingsTests;

public class ResolveBaseUrl
{
    [Fact]
    public void DerivesUnitedStatesHostFromSubdomainWhenNoOverrideIsSet()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4", Environment = "US" };

        Assert.Equal("https://cp-exp-4.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesEuropeanHostFromSubdomainForTheEuRegion()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4", Environment = "eu" };

        Assert.Equal("https://cp-exp-4.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("https://sandbox.example.test")]
    public void ExplicitOverrideWinsOverTheSubdomainDerivedHost(string overrideUrl)
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-4",
            Environment = "US",
            BaseUrl = overrideUrl
        };

        Assert.Equal(overrideUrl, settings.ResolveBaseUrl());
        Assert.True(settings.HasExplicitBaseUrl);
    }

    [Fact]
    public void TreatsAnEmptyOverrideAsAbsentSoTheDerivedHostIsUsed()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4", BaseUrl = "   " };

        Assert.False(settings.HasExplicitBaseUrl);
        Assert.Equal("https://cp-exp-4.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void RefusesToGuessAHostWhenNeitherOverrideNorSubdomainIsConfigured()
    {
        var settings = new MaxioSettings();

        Assert.False(settings.TryResolveBaseUrl(out var resolved));
        Assert.Equal(string.Empty, resolved);
        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());
    }
}
