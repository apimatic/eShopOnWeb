using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The configurable target server (plan.md §2.3): an explicit <c>Maxio:BaseUrl</c> must win verbatim,
/// and the subdomain-derived host is only the fallback.
/// </summary>
public class MaxioSettingsTests
{
    private static MaxioSettings Configured(string? baseUrl, string environment = "US") => new()
    {
        ApiKey = "key",
        Subdomain = "cp-exp-1",
        Environment = environment,
        BaseUrl = baseUrl,
        ProductFamilyHandle = "eshop-subscribe",
        MeteredComponentHandle = "api-call"
    };

    [Fact]
    public void ResolveBaseUrl_DerivesTheUsHost_WhenNoOverrideIsConfigured()
    {
        var settings = Configured(baseUrl: null);

        Assert.Equal("https://cp-exp-1.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesTheEuHost_WhenTheRegionIsEu()
    {
        var settings = Configured(baseUrl: null, environment: "eu");

        Assert.Equal("https://cp-exp-1.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("https://dev-tenant.chargify.com")]
    [InlineData("https://production.example.com")]
    public void ResolveBaseUrl_UsesAnExplicitOverrideVerbatim_AndIgnoresTheSubdomain(string configured)
    {
        var settings = Configured(configured);

        var resolved = settings.ResolveBaseUrl();

        Assert.Equal(configured, resolved);
        Assert.DoesNotContain("cp-exp-1", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveBaseUrl_OverrideWinsInEitherRegion_SoRetargetingIsRegionIndependent()
    {
        Assert.Equal("http://localhost:8080", Configured("http://localhost:8080", "US").ResolveBaseUrl());
        Assert.Equal("http://localhost:8080", Configured("http://localhost:8080", "EU").ResolveBaseUrl());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ResolveBaseUrl_TreatsABlankOverrideAsAbsent(string? blank)
    {
        Assert.Equal("https://cp-exp-1.chargify.com", Configured(blank).ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_Throws_WhenNeitherAnOverrideNorASubdomainIsConfigured()
    {
        var settings = Configured(baseUrl: null);
        settings.Subdomain = string.Empty;

        Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
    }

    [Fact]
    public void Validate_Throws_AndNamesEveryMissingSetting()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<BillingConfigurationException>(settings.Validate);

        Assert.Contains("Maxio:ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Maxio:Subdomain", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Maxio:MeteredComponentHandle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AcceptsABaseUrlWithoutASubdomain_SoAMockHostNeedsNoTenant()
    {
        var settings = Configured("http://localhost:8080");
        settings.Subdomain = string.Empty;

        settings.Validate();

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void Validate_Throws_WhenTheApiKeyIsMissing()
    {
        var settings = Configured(baseUrl: null);
        settings.ApiKey = string.Empty;

        var exception = Assert.Throws<BillingConfigurationException>(settings.Validate);

        Assert.Contains("Maxio:ApiKey", exception.Message, StringComparison.Ordinal);
    }
}
