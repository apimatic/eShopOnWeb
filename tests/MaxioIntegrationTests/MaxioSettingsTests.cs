using MaxioAdvancedBilling;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Exercises the target-server resolution rule that plan.md §2.3 calls a hard requirement: an
/// explicit <see cref="MaxioSettings.BaseUrl"/> always wins over the subdomain-derived host, and
/// the client must never silently fall back to a hardcoded host.
/// </summary>
public class MaxioSettingsTests
{
    [Fact]
    public void ApplyTo_UsesExplicitBaseUrl_WhenSet()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key-123",
            Subdomain = "should-be-ignored",
            Environment = "US",
            BaseUrl = "http://localhost:8080"
        };
        var options = new MaxioAdvancedBillingClientOptions();

        settings.ApplyTo(options);

        Assert.Equal("http://localhost:8080", options.Server.Production.Us.BaseUrl);
    }

    [Fact]
    public void ApplyTo_DerivesFromSubdomain_WhenBaseUrlIsEmpty()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key-123",
            Subdomain = "apimatic-hackathon",
            Environment = "US",
            BaseUrl = null
        };
        var options = new MaxioAdvancedBillingClientOptions();

        settings.ApplyTo(options);

        Assert.Equal("apimatic-hackathon", options.Server.Production.Us.Site);
    }

    [Fact]
    public void ApplyTo_DerivesFromSubdomain_WhenBaseUrlIsWhitespace()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key-123",
            Subdomain = "apimatic-hackathon",
            Environment = "US",
            BaseUrl = "   "
        };
        var options = new MaxioAdvancedBillingClientOptions();

        settings.ApplyTo(options);

        Assert.Equal("apimatic-hackathon", options.Server.Production.Us.Site);
    }

    [Fact]
    public void ApplyTo_TargetsEuServer_WhenEnvironmentIsEu()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key-123",
            Subdomain = "eu-tenant",
            Environment = "EU",
            BaseUrl = null
        };
        var options = new MaxioAdvancedBillingClientOptions();

        settings.ApplyTo(options);

        Assert.Equal(MaxioAdvancedBilling.Servers.ServerEnvironment.Eu, options.Environment);
        Assert.Equal("eu-tenant", options.Server.Production.Eu.Site);
    }

    [Fact]
    public void ApplyTo_UsesExplicitBaseUrl_OnEuServer_WhenBothSetAndRegionIsEu()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "key-123",
            Subdomain = "should-be-ignored",
            Environment = "EU",
            BaseUrl = "https://mock.example.com"
        };
        var options = new MaxioAdvancedBillingClientOptions();

        settings.ApplyTo(options);

        Assert.Equal("https://mock.example.com", options.Server.Production.Eu.BaseUrl);
    }

    [Fact]
    public void ApplyTo_SetsBasicAuthFromApiKey()
    {
        var settings = new MaxioSettings { ApiKey = "the-api-key", Subdomain = "site" };
        var options = new MaxioAdvancedBillingClientOptions();

        settings.ApplyTo(options);

        Assert.NotNull(options.BasicAuth);
        Assert.Equal("the-api-key", options.BasicAuth!.Username);
        Assert.Equal("x", options.BasicAuth!.Password);
    }
}
