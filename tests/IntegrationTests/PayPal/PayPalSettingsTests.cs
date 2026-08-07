using System;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.PayPal;

public class PayPalSettingsTests
{
    [Fact]
    public void ResolvesSandboxBaseUrlFromEnvironment()
    {
        var settings = new PayPalSettings { Environment = "sandbox", ClientId = "id", ClientSecret = "secret" };

        Assert.Equal("https://api-m.sandbox.paypal.com", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlWinsAndIsUsedVerbatim()
    {
        var settings = new PayPalSettings
        {
            Environment = "sandbox",
            BaseUrl = "https://proxy.internal/paypal",
            ClientId = "id",
            ClientSecret = "secret"
        };

        Assert.Equal("https://proxy.internal/paypal", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ValidateThrowsWhenCredentialsMissing()
    {
        var settings = new PayPalSettings { Environment = "sandbox" };

        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void ResolveThrowsForUnknownEnvironment()
    {
        var settings = new PayPalSettings { Environment = "staging", ClientId = "id", ClientSecret = "secret" };

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());
    }
}
