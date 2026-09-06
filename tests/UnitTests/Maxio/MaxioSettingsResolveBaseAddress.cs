using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioSettingsResolveBaseAddress
{
    private static MaxioSettings Settings(string? subdomain = "acme", string? baseUrl = null, string? environment = null) => new()
    {
        ApiKey = "key",
        ProductFamilyHandle = "family",
        Subdomain = subdomain,
        BaseUrl = baseUrl,
        Environment = environment
    };

    [Fact]
    public void SubstitutesTheSubdomainIntoTheUsServerTemplate()
    {
        Assert.Equal(new Uri("https://acme.chargify.com/"), Settings().ResolveBaseAddress());
    }

    [Fact]
    public void UsesTheEuServerTemplateForEuHostedSites()
    {
        Assert.Equal(new Uri("https://acme.ebilling.maxio.com/"), Settings(environment: "eu").ResolveBaseAddress());
    }

    [Fact]
    public void UsesTheBaseUrlOverrideVerbatim()
    {
        var settings = Settings(subdomain: "ignored", baseUrl: "https://billing.internal.example/maxio/");

        Assert.Equal(new Uri("https://billing.internal.example/maxio/"), settings.ResolveBaseAddress());
    }

    [Fact]
    public void AppendsATrailingSlashSoRelativePathsResolveUnderTheOverride()
    {
        var settings = Settings(baseUrl: "https://billing.internal.example/maxio");

        Assert.Equal(new Uri("https://billing.internal.example/maxio/tenant.json"), new Uri(settings.ResolveBaseAddress(), "tenant.json"));
    }

    [Fact]
    public void RequiresASubdomainWhenNoOverrideIsSupplied()
    {
        var settings = Settings(subdomain: "   ");

        var exception = Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseAddress());
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void RejectsARelativeBaseUrlOverride()
    {
        var settings = Settings(baseUrl: "/maxio");

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseAddress());
    }

    [Fact]
    public void RejectsAnUnknownEnvironment()
    {
        var settings = Settings(environment: "APAC");

        Assert.Throws<InvalidOperationException>(() => settings.ResolveBaseAddress());
    }
}
