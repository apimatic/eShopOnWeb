using System;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void DerivesUsEnvironmentUrlFromSubdomain()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4" };

        Assert.Equal("https://cp-exp-4.chargify.com/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void PrefersExplicitBaseUrlOverride_Verbatim()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored",
            BaseUrl = "https://billing.internal.example/custom/path",
        };

        Assert.Equal("https://billing.internal.example/custom/path/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void KeepsTrailingSlashOfOverride()
    {
        var settings = new MaxioSettings { BaseUrl = "https://billing.example/" };

        Assert.Equal("https://billing.example/", settings.ResolveBaseUrl());
    }

    [Fact]
    public void Throws_WhenNeitherSubdomainNorBaseUrlIsConfigured()
    {
        var settings = new MaxioSettings();

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseUrl());
    }
}
