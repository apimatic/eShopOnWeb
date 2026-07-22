using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The outbound target must be configuration-driven: an explicit base URL always wins so the same
/// build can be pointed at production, a dev tenant, or a local mock (plan.md §2.3).
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void DerivesTheUsHostFromTheSubdomainWhenNoOverrideIsSet()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1", Environment = "US" };

        Assert.Equal("https://cp-exp-1.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesTheEuHostForTheEuRegion()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1", Environment = "EU" };

        Assert.Equal("https://cp-exp-1.ebilling.maxio.com/", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("us")]
    [InlineData("Us")]
    public void TreatsTheRegionCaseInsensitively(string region)
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1", Environment = region };

        Assert.Equal("https://cp-exp-1.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void AnExplicitBaseUrlWinsOverTheSubdomainDerivedHost()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-1",
            Environment = "US",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void AnExplicitBaseUrlWinsEvenForTheEuRegion()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-1",
            Environment = "EU",
            BaseUrl = "https://staging.example.com"
        };

        Assert.Equal("https://staging.example.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void PreservesAPathOnTheOverrideSoAMockCanBeMountedUnderAPrefix()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1", BaseUrl = "http://localhost:8080/maxio/" };

        Assert.Equal("http://localhost:8080/maxio/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void AddsTheTrailingSlashRelativeRequestPathsNeed()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1", BaseUrl = "http://localhost:8080/maxio" };

        var resolved = settings.ResolveBaseUrl();

        Assert.EndsWith("/", resolved);
        // Without the trailing slash, "products.json" would replace the "/maxio" segment entirely.
        Assert.Equal("http://localhost:8080/maxio/products.json", new Uri(new Uri(resolved), "products.json").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FallsBackToTheSubdomainWhenTheOverrideIsBlank(string? baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-1", BaseUrl = baseUrl };

        Assert.Equal("https://cp-exp-1.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void FailsLoudlyWhenNeitherAnOverrideNorASubdomainIsConfigured()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("Maxio:BaseUrl", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void DefaultsToInvoiceBillingSoEnrolmentNeedsNoStoredCard()
    {
        Assert.Equal("remittance", new MaxioSettings().PaymentCollectionMethod);
    }
}
