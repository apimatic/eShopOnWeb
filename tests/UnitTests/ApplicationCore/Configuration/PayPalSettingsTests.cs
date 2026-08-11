using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Configuration;

public class PayPalSettingsTests
{
    [Fact]
    public void SandboxEnvironmentResolvesToSandboxHost()
    {
        var settings = new PayPalSettings { Environment = "sandbox" };
        Assert.Equal("https://api-m.sandbox.paypal.com/", settings.ResolveBaseUrl());
    }

    [Theory]
    [InlineData("live")]
    [InlineData("production")]
    public void NonSandboxEnvironmentResolvesToLiveHost(string environment)
    {
        var settings = new PayPalSettings { Environment = environment };
        Assert.Equal("https://api-m.paypal.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlIsUsedVerbatimAndTrailingSlashEnsured()
    {
        var settings = new PayPalSettings { Environment = "sandbox", BaseUrl = "https://proxy.internal/paypal" };
        Assert.Equal("https://proxy.internal/paypal/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void ExplicitBaseUrlWithTrailingSlashIsPreserved()
    {
        var settings = new PayPalSettings { BaseUrl = "https://proxy.internal/" };
        Assert.Equal("https://proxy.internal/", settings.ResolveBaseUrl());
    }
}
