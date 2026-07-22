using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class MaxioSettingsTests
{
    [Fact]
    public void ExplicitBaseUrlWinsOverTheSubdomain()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "apimatic-hackathon",
            BaseUrl = "http://localhost:8080"
        };

        Assert.Equal("http://localhost:8080/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlKeepsItsPath()
    {
        var settings = new MaxioSettings { BaseUrl = "https://gateway.internal/maxio" };

        Assert.Equal("https://gateway.internal/maxio/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void BlankBaseUrlFallsBackToTheSubdomainDerivedHost()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", BaseUrl = "  " };

        Assert.Equal("https://apimatic-hackathon.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void EuropeanRegionDerivesTheEuropeanHost()
    {
        var settings = new MaxioSettings { Subdomain = "apimatic-hackathon", Environment = "eu" };

        Assert.Equal("https://apimatic-hackathon.ebilling.maxio.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void WithoutABaseUrlOrSubdomainTheTargetIsAConfigurationError()
    {
        var settings = new MaxioSettings();

        Assert.Throws<BillingConfigurationException>(() => settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRejectsAMissingApiKey(string? apiKey)
    {
        var settings = ValidSettings();
        settings.ApiKey = apiKey;

        var exception = Assert.Throws<BillingConfigurationException>(settings.Validate);
        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    [Fact]
    public void ValidateRejectsAMissingMeteredComponentHandle()
    {
        var settings = ValidSettings();
        settings.MeteredComponentHandle = string.Empty;

        var exception = Assert.Throws<BillingConfigurationException>(settings.Validate);
        Assert.Contains("MeteredComponentHandle", exception.Message);
    }

    [Fact]
    public void ValidateNeverEchoesTheApiKey()
    {
        var settings = ValidSettings();
        settings.ProductFamilyHandle = string.Empty;

        var exception = Assert.Throws<BillingConfigurationException>(settings.Validate);
        Assert.DoesNotContain(settings.ApiKey!, exception.Message);
    }

    [Fact]
    public void ValidateAcceptsAFullyConfiguredIntegration()
    {
        ValidSettings().Validate();
    }

    private static MaxioSettings ValidSettings() => new()
    {
        ApiKey = "super-secret-key",
        Subdomain = "apimatic-hackathon",
        ProductFamilyHandle = "eshop-subscribe",
        DefaultProductHandle = "eshop-pro",
        MeteredComponentHandle = "api-call"
    };
}
