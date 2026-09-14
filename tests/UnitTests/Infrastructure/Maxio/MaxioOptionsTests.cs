using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUri_DerivesFromSubdomain_WhenBaseUrlNotSet()
    {
        var options = new MaxioOptions { Subdomain = "acme", ApiKey = "k", ProductFamilyHandle = "fam" };

        Assert.Equal(new Uri("https://acme.chargify.com/"), options.ResolveBaseUri());
    }

    [Fact]
    public void ResolveBaseUri_UsesBaseUrlVerbatim_WhenSet()
    {
        // The Maxio:BaseUrl override must be used verbatim, even when a subdomain is present.
        var options = new MaxioOptions
        {
            Subdomain = "acme",
            BaseUrl = "https://proxy.internal.test/maxio/",
            ApiKey = "k",
            ProductFamilyHandle = "fam"
        };

        Assert.Equal(new Uri("https://proxy.internal.test/maxio/"), options.ResolveBaseUri());
    }

    [Fact]
    public void ResolveBaseUri_AppendsTrailingSlash_WhenMissing()
    {
        var options = new MaxioOptions { BaseUrl = "https://proxy.internal.test/maxio", ApiKey = "k", ProductFamilyHandle = "fam" };

        Assert.Equal(new Uri("https://proxy.internal.test/maxio/"), options.ResolveBaseUri());
    }

    [Fact]
    public void Validate_Passes_WhenApiKeySubdomainAndFamilyPresent()
    {
        var options = new MaxioOptions { ApiKey = "k", Subdomain = "acme", ProductFamilyHandle = "fam" };

        var exception = Record.Exception(options.Validate);

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_Passes_WhenBaseUrlSuppliedInsteadOfSubdomain()
    {
        var options = new MaxioOptions { ApiKey = "k", BaseUrl = "https://x.test/", ProductFamilyHandle = "fam" };

        var exception = Record.Exception(options.Validate);

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("", "acme", "fam")]   // missing api key
    [InlineData("k", "", "fam")]      // missing subdomain and base url
    [InlineData("k", "acme", "")]     // missing product family handle
    public void Validate_Throws_WhenRequiredSettingMissing(string apiKey, string subdomain, string familyHandle)
    {
        var options = new MaxioOptions { ApiKey = apiKey, Subdomain = subdomain, ProductFamilyHandle = familyHandle };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
