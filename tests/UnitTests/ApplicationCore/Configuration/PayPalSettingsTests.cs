using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Configuration;

public class PayPalSettingsTests
{
    [Fact]
    public void ResolveBaseUrl_DefaultsToSandbox()
    {
        var settings = new PayPalSettings { Environment = "sandbox" };
        Assert.Equal("https://api-m.sandbox.paypal.com", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("live")]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    public void ResolveBaseUrl_UsesLiveForProduction(string environment)
    {
        var settings = new PayPalSettings { Environment = environment };
        Assert.Equal("https://api-m.paypal.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_ExplicitOverride_WinsAndIsTrimmed()
    {
        var settings = new PayPalSettings
        {
            Environment = "live",
            BaseUrl = "https://proxy.internal/paypal/"
        };
        // Override is used verbatim (trailing slash trimmed) regardless of environment.
        Assert.Equal("https://proxy.internal/paypal", settings.ResolveBaseUrl());
    }

    [Fact]
    public void Validate_Throws_WhenCredentialsMissing()
    {
        var settings = new PayPalSettings { Currency = "USD" };
        Assert.ThrowsAny<System.Exception>(() => settings.Validate());
    }
}
