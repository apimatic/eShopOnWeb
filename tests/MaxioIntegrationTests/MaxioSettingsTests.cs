using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The target server must be switchable purely through configuration, and an explicitly
/// configured base URL must always win over the subdomain-derived host.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_PrefersTheExplicitOverrideOverTheSubdomain()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            Environment = "US",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_UsesTheOverrideVerbatimWithoutRewritingIt()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored-when-overridden",
            BaseUrl = "https://billing.internal.example.com/api"
        };

        Assert.Equal("https://billing.internal.example.com/api", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveBaseUrl_DerivesTheUnitedStatesHostWhenNoOverrideIsConfigured(string? baseUrl)
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            Environment = "US",
            BaseUrl = baseUrl
        };

        Assert.Equal("https://apimatic-hackathon.chargify.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesTheEuropeanHostForTheEuRegion()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            Environment = "eu"
        };

        Assert.Equal("https://apimatic-hackathon.ebilling.maxio.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_TrimsSurroundingWhitespaceFromTheOverride()
    {
        var settings = new MaxioSettings { BaseUrl = "  http://localhost:9090  " };

        Assert.Equal("http://localhost:9090", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("localhost:8080")]      // Parses as an absolute URI, but with a bogus scheme.
    [InlineData("/relative/path")]
    [InlineData("ftp://billing.example.com")]
    [InlineData("not a url at all")]
    public void ResolveBaseUrl_RejectsAnOverrideThatIsNotAnAbsoluteHttpUrl(string baseUrl)
    {
        var settings = new MaxioSettings { Subdomain = "site", BaseUrl = baseUrl };

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());

        Assert.Contains("not a valid absolute http or https URL", exception.Message);
    }

    [Fact]
    public void ResolveBaseUrl_RejectsAConfigurationWithNeitherAnOverrideNorASubdomain()
    {
        var settings = new MaxioSettings();

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());

        Assert.Contains("BaseUrl", exception.Message);
        Assert.Contains("Subdomain", exception.Message);
    }

    [Fact]
    public void IsEuropeanRegion_IgnoresCasing()
    {
        Assert.True(new MaxioSettings { Environment = "eu" }.IsEuropeanRegion);
        Assert.True(new MaxioSettings { Environment = "EU" }.IsEuropeanRegion);
        Assert.False(new MaxioSettings { Environment = "US" }.IsEuropeanRegion);
    }
}
