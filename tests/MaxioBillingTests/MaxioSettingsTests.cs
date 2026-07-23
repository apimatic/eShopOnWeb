using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// The configurable-target-server requirement (plan.md §2.3): an explicit base URL always wins, and the
/// derived host is only a fallback. These tests fail if that resolution order is ever inverted.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesExplicitOverride_Verbatim()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-2",
            BaseUrl = "http://localhost:8080"
        };

        Assert.True(settings.HasExplicitBaseUrl);
        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_ExplicitOverride_BeatsSubdomain()
    {
        var derived = new MaxioSettings { Subdomain = "cp-exp-2" }.ResolveBaseUrl();
        var overridden = new MaxioSettings { Subdomain = "cp-exp-2", BaseUrl = "https://staging.example.test" }
            .ResolveBaseUrl();

        Assert.NotEqual(derived, overridden);
        Assert.Equal("https://staging.example.test", overridden);
    }

    [Fact]
    public void ResolveBaseUrl_DerivesUsHost_FromSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2", Environment = "US" };

        Assert.False(settings.HasExplicitBaseUrl);
        Assert.Equal("https://cp-exp-2.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesEuHost_WhenRegionIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-2", Environment = "eu" };

        Assert.True(settings.IsEuRegion);
        Assert.Equal("https://cp-exp-2.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_TrimsWhitespace()
    {
        Assert.Equal("https://cp-exp-2.chargify.com", new MaxioSettings { Subdomain = "  cp-exp-2  " }.ResolveBaseUrl());
        Assert.Equal("http://localhost:8080", new MaxioSettings { BaseUrl = "  http://localhost:8080  " }.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_Throws_WhenNeitherBaseUrlNorSubdomainIsConfigured()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new MaxioSettings().ResolveBaseUrl());

        Assert.Contains("Maxio:BaseUrl", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void Validate_Throws_WhenApiKeyIsMissing()
    {
        var settings = BillingTestContext.DefaultSettings();
        settings.ApiKey = string.Empty;

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);

        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    [Fact]
    public void Validate_Throws_WhenBaseUrlIsNotAbsolute()
    {
        var settings = BillingTestContext.DefaultSettings();
        settings.BaseUrl = "not-a-url";

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);

        Assert.Contains("absolute URL", exception.Message);
    }

    [Fact]
    public void Validate_Throws_WhenMeteredComponentHandleIsMissing()
    {
        var settings = BillingTestContext.DefaultSettings();
        settings.MeteredComponentHandle = "  ";

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);

        Assert.Contains("MeteredComponentHandle", exception.Message);
    }

    [Fact]
    public void Validate_Passes_ForAFullyConfiguredTenant()
    {
        var settings = BillingTestContext.DefaultSettings();
        settings.BaseUrl = null;

        settings.Validate();

        Assert.Equal("https://cp-exp-2.chargify.com", settings.ResolveBaseUrl());
    }
}
