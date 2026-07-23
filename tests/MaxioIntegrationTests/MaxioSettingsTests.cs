using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The outbound target server must be configurable: the same build has to reach production, a
/// dev/sandbox tenant, or a local mock purely through configuration.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_DerivesUsHost_FromSubdomain_WhenNoExplicitBaseUrl()
    {
        var settings = new MaxioSettings { Subdomain = "acme", Environment = "US" };

        Assert.Equal("https://acme.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesEuHost_WhenRegionIsEu()
    {
        var settings = new MaxioSettings { Subdomain = "acme", Environment = "EU" };

        Assert.Equal("https://acme.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_ExplicitBaseUrl_WinsOverSubdomain()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "acme",
            Environment = "US",
            BaseUrl = "https://staging.example.com"
        };

        // The override must win verbatim; the subdomain-derived host must not leak through.
        Assert.Equal("https://staging.example.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_HonoursLocalMockServer()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "acme",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_TrimsTrailingSlash()
    {
        var settings = new MaxioSettings { BaseUrl = "https://staging.example.com/" };

        Assert.Equal("https://staging.example.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveBaseUrl_FallsBackToSubdomain_WhenBaseUrlIsBlank(string? blank)
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = blank };

        Assert.Equal("https://acme.chargify.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("example.com")]
    public void ResolveBaseUrl_Throws_WhenExplicitBaseUrlIsNotAnAbsoluteHttpUrl(string bad)
    {
        var settings = new MaxioSettings { Subdomain = "acme", BaseUrl = bad };

        var ex = Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("BaseUrl", ex.Message);
    }

    [Fact]
    public void ResolveBaseUrl_Throws_WhenNeitherBaseUrlNorSubdomainConfigured()
    {
        var settings = new MaxioSettings();

        Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
    }

    [Fact]
    public void Validate_Throws_WhenApiKeyMissing()
    {
        var settings = BillingClientFixture.DefaultSettings();
        settings.ApiKey = null;

        var ex = Assert.Throws<BillingConfigurationException>(settings.Validate);
        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public void Validate_Throws_WhenProductFamilyHandleMissing()
    {
        var settings = BillingClientFixture.DefaultSettings();
        settings.ProductFamilyHandle = null;

        var ex = Assert.Throws<BillingConfigurationException>(settings.Validate);
        Assert.Contains("ProductFamilyHandle", ex.Message);
    }

    [Fact]
    public void Validate_Throws_WhenMeteredComponentHandleMissing()
    {
        var settings = BillingClientFixture.DefaultSettings();
        settings.MeteredComponentHandle = null;

        var ex = Assert.Throws<BillingConfigurationException>(settings.Validate);
        Assert.Contains("MeteredComponentHandle", ex.Message);
    }

    [Fact]
    public void Validate_Passes_ForAFullyConfiguredSite()
    {
        var settings = BillingClientFixture.DefaultSettings();

        settings.Validate();
    }

    [Fact]
    public void IsEuRegion_IsCaseInsensitive_AndDefaultsToUs()
    {
        Assert.True(new MaxioSettings { Environment = "eu" }.IsEuRegion);
        Assert.True(new MaxioSettings { Environment = " EU " }.IsEuRegion);
        Assert.False(new MaxioSettings { Environment = "US" }.IsEuRegion);
        Assert.False(new MaxioSettings().IsEuRegion);
    }

    [Fact]
    public async Task Client_SendsRequestsToTheConfiguredMockServer_NotTheSubdomainHost()
    {
        var settings = BillingClientFixture.DefaultSettings();
        settings.BaseUrl = "http://localhost:8080";

        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.ProductList());
        var client = BillingClientFixture.Create(handler, settings);

        await client.ListPlansAsync();

        // Proves the override reaches the wire, not just the settings object.
        Assert.Equal("localhost", handler.LastRequest.Uri.Host);
        Assert.Equal(8080, handler.LastRequest.Uri.Port);
    }

    [Fact]
    public async Task Client_SendsRequestsToTheSubdomainHost_WhenNoOverrideConfigured()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.ProductList());
        var client = BillingClientFixture.Create(handler);

        await client.ListPlansAsync();

        Assert.Equal($"{BillingClientFixture.TestSubdomain}.chargify.com", handler.LastRequest.Uri.Host);
    }
}
