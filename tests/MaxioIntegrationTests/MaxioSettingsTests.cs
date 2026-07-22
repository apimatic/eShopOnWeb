using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The target server must be switchable purely through configuration: an explicit base URL always wins,
/// otherwise the host is derived from the site subdomain and the region.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void Derives_us_host_from_subdomain_when_no_override_is_configured()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", Environment = "US" };

        Assert.Equal("https://cp-exp-3.chargify.com", settings.ResolveBaseUrl());
        Assert.False(settings.HasExplicitBaseUrl);
    }

    [Fact]
    public void Derives_eu_host_from_subdomain_when_region_is_eu()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", Environment = "eu" };

        Assert.Equal("https://cp-exp-3.ebilling.maxio.com", settings.ResolveBaseUrl());
        Assert.True(settings.IsEuropeanRegion);
    }

    [Fact]
    public void Explicit_base_url_wins_over_the_subdomain_derived_host()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-3",
            Environment = "US",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
        Assert.True(settings.HasExplicitBaseUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_base_url_is_not_treated_as_an_override(string? baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-3", Environment = "US", BaseUrl = baseUrl };

        Assert.False(settings.HasExplicitBaseUrl);
        Assert.Equal("https://cp-exp-3.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void Reports_a_configuration_error_when_neither_base_url_nor_subdomain_is_set()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());

        Assert.Contains("BaseUrl", exception.Message);
        Assert.Contains("Subdomain", exception.Message);
    }

    [Fact]
    public void Client_options_target_the_explicit_base_url_on_the_selected_region()
    {
        var settings = MaxioTestContext.Settings(baseUrl: "http://localhost:8080");

        var options = Infrastructure.Services.MaxioBillingClient.CreateClientOptions(settings);

        Assert.Equal("http://localhost:8080", options.Server.Production.Us.BaseUrl);
    }

    [Fact]
    public void Client_options_target_the_subdomain_when_no_override_is_configured()
    {
        var settings = MaxioTestContext.Settings();

        var options = Infrastructure.Services.MaxioBillingClient.CreateClientOptions(settings);

        Assert.Equal("test-site", options.Server.Production.Us.Site);
    }
}
