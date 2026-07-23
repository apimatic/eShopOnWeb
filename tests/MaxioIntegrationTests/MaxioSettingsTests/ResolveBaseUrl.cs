using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioSettingsTests;

/// <summary>
/// The outbound target must be switchable through configuration alone: an explicit base URL always
/// wins, and only in its absence is the host derived from the subdomain and region.
/// </summary>
public class ResolveBaseUrl
{
    [Fact]
    public void DerivesUsHostFromSubdomainWhenNoOverrideIsSet()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4", Environment = "US" };

        Assert.Equal(new Uri("https://cp-exp-4.chargify.com"), settings.ResolveBaseUrl());
    }

    [Fact]
    public void DerivesEuHostForTheEuropeanRegion()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4", Environment = "EU" };

        Assert.Equal(new Uri("https://cp-exp-4.ebilling.maxio.com"), settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("us")]
    [InlineData("Us")]
    [InlineData("")]
    [InlineData("something-else")]
    public void FallsBackToTheUsHostForAnyNonEuropeanRegion(string environment)
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4", Environment = environment };

        Assert.Equal(new Uri("https://cp-exp-4.chargify.com"), settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlWinsOverTheSubdomainDerivedHost()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-4",
            Environment = "US",
            BaseUrl = "https://some-other-tenant.chargify.com"
        };

        Assert.Equal(new Uri("https://some-other-tenant.chargify.com"), settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlWinsEvenForTheEuropeanRegion()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "cp-exp-4",
            Environment = "EU",
            BaseUrl = "https://pinned.example.com"
        };

        Assert.Equal(new Uri("https://pinned.example.com"), settings.ResolveBaseUrl());
    }

    /// <summary>
    /// The same build must be able to target a local mock server purely through configuration —
    /// plain HTTP on a non-standard port, taken verbatim.
    /// </summary>
    [Fact]
    public void HonoursAPlainHttpLocalMockServerVerbatim()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4", BaseUrl = "http://localhost:8080" };

        var resolved = settings.ResolveBaseUrl();

        Assert.Equal(new Uri("http://localhost:8080"), resolved);
        Assert.Equal("http", resolved.Scheme);
        Assert.Equal(8080, resolved.Port);
    }

    [Fact]
    public void TrimsSurroundingWhitespaceOnAnExplicitBaseUrl()
    {
        var settings = new MaxioSettings { BaseUrl = "  https://localhost:8080  " };

        Assert.Equal(new Uri("https://localhost:8080"), settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TreatsABlankBaseUrlAsAbsentAndDerivesTheHost(string? baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4", BaseUrl = baseUrl };

        Assert.Equal(new Uri("https://cp-exp-4.chargify.com"), settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("cp-exp-4.chargify.com")]
    [InlineData("ftp://cp-exp-4.chargify.com")]
    [InlineData("file:///etc/passwd")]
    public void RejectsABaseUrlThatIsNotAnAbsoluteHttpUrl(string baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4", BaseUrl = baseUrl };

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("absolute http(s) URL", exception.Message);
    }

    [Fact]
    public void RejectsConfigurationWithNeitherABaseUrlNorASubdomain()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
        Assert.Contains("Maxio:BaseUrl", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void DefaultsToRemittanceCollectionSoASiteWithNoGatewayCanStillSubscribe()
    {
        Assert.Equal("remittance", new MaxioSettings().PaymentCollectionMethod);
    }
}
