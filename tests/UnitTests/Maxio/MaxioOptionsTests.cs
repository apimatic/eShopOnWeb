using System;
using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseAddress_DerivesFromSubdomain_UsingTheSpecServerTemplate()
    {
        var options = new MaxioOptions { ApiKey = "key", Subdomain = "acme", ProductFamilyHandle = "family" };

        Assert.Equal(new Uri("https://acme.chargify.com/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddress_PrefersBaseUrl_AndUsesItVerbatim()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "ignored",
            ProductFamilyHandle = "family",
            BaseUrl = "https://acme.ebilling.maxio.com"
        };

        Assert.Equal(new Uri("https://acme.ebilling.maxio.com/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void ResolveBaseAddress_KeepsAnExistingTrailingSlash()
    {
        var options = new MaxioOptions { ApiKey = "key", BaseUrl = "https://acme.ebilling.maxio.com/" };

        Assert.Equal(new Uri("https://acme.ebilling.maxio.com/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void Validate_PassesForACompleteConfiguration()
    {
        var options = new MaxioOptions { ApiKey = "key", Subdomain = "acme", ProductFamilyHandle = "family" };

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Validate_ReportsEveryMissingSetting()
    {
        var failures = new MaxioOptions().Validate();

        Assert.Contains(failures, failure => failure.Contains("Maxio:ApiKey"));
        Assert.Contains(failures, failure => failure.Contains("Maxio:Subdomain"));
        Assert.Contains(failures, failure => failure.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void Validate_AcceptsBaseUrlInPlaceOfSubdomain()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            ProductFamilyHandle = "family",
            BaseUrl = "https://acme.ebilling.maxio.com"
        };

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Validate_RejectsANonAbsoluteBaseUrl()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = "family",
            BaseUrl = "not-a-url"
        };

        Assert.Contains(options.Validate(), failure => failure.Contains("absolute URL"));
    }

    [Fact]
    public void Validate_TreatsAnEmptyBaseUrlAsUnset()
    {
        var options = new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "acme",
            ProductFamilyHandle = "family",
            BaseUrl = "   "
        };

        Assert.Empty(options.Validate());
        Assert.Equal(new Uri("https://acme.chargify.com/"), options.ResolveBaseAddress());
    }
}
