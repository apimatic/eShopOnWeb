using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioSettingsTests;

/// <summary>
/// Misconfiguration must surface as a clear, actionable error at composition time rather than as an
/// unauthorized call later.
/// </summary>
public class Validate
{
    private static MaxioSettings Complete() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        Environment = "US",
        ProductFamilyHandle = "eshop-subscribe",
        MeteredComponentHandle = "api-call"
    };

    [Fact]
    public void AcceptsACompleteConfiguration()
    {
        var exception = Record.Exception(() => Complete().Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void ThrowsWhenTheApiKeyIsMissing()
    {
        var settings = Complete();
        settings.ApiKey = null;

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.Validate());

        Assert.Contains("Maxio:ApiKey", exception.Message);
        // The message must tell an operator how to fix it, not merely that it is broken.
        Assert.Contains("user-secrets", exception.Message);
    }

    [Fact]
    public void ThrowsWhenTheProductFamilyHandleIsMissing()
    {
        var settings = Complete();
        settings.ProductFamilyHandle = "  ";

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.Validate());

        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }

    [Fact]
    public void ThrowsWhenTheMeteredComponentHandleIsMissing()
    {
        var settings = Complete();
        settings.MeteredComponentHandle = null;

        var exception = Assert.Throws<BillingConfigurationException>(() => settings.Validate());

        Assert.Contains("Maxio:MeteredComponentHandle", exception.Message);
    }

    [Fact]
    public void ThrowsWhenThereIsNoHostToCall()
    {
        var settings = Complete();
        settings.Subdomain = null;
        settings.BaseUrl = null;

        Assert.Throws<BillingConfigurationException>(() => settings.Validate());
    }

    [Theory]
    [InlineData("EU", true)]
    [InlineData("eu", true)]
    [InlineData(" EU ", true)]
    [InlineData("US", false)]
    [InlineData("", false)]
    public void IdentifiesTheEuRegionCaseAndWhitespaceInsensitively(string environment, bool expected)
    {
        var settings = new MaxioSettings { Environment = environment };

        Assert.Equal(expected, settings.IsEuRegion);
    }
}
