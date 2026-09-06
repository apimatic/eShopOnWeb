using System;
using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioOptionsTests
{
    private static MaxioOptions Valid() => new()
    {
        ApiKey = "not-a-real-key",
        Subdomain = "example-site",
        ProductFamilyHandle = "example-family"
    };

    [Fact]
    public void DerivesTheBaseAddressFromTheSubdomainUsingTheSpecificationServerTemplate()
    {
        var options = Valid();

        Assert.Equal(new Uri("https://example-site.chargify.com/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void UsesTheBaseUrlOverrideVerbatimWhenItIsSet()
    {
        var options = Valid();
        options.BaseUrl = "https://example-site.ebilling.maxio.com";

        Assert.Equal(new Uri("https://example-site.ebilling.maxio.com/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void DoesNotDoubleTheTrailingSlashOnABaseUrlOverride()
    {
        var options = Valid();
        options.BaseUrl = "https://proxy.internal/maxio/";

        Assert.Equal(new Uri("https://proxy.internal/maxio/"), options.ResolveBaseAddress());
    }

    [Fact]
    public void AcceptsAFullyPopulatedConfiguration()
    {
        Assert.Empty(Valid().Validate());
    }

    [Fact]
    public void ReportsEveryMissingRequiredKeyByName()
    {
        var failures = new MaxioOptions().Validate();

        Assert.Contains(failures, f => f.Contains("Maxio:ApiKey"));
        Assert.Contains(failures, f => f.Contains("Maxio:ProductFamilyHandle"));
        Assert.Contains(failures, f => f.Contains("Maxio:Subdomain"));
    }

    [Fact]
    public void AcceptsABaseUrlInsteadOfASubdomain()
    {
        var options = new MaxioOptions
        {
            ApiKey = "not-a-real-key",
            ProductFamilyHandle = "example-family",
            BaseUrl = "https://example-site.chargify.com"
        };

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAnAbsoluteHttpUrl()
    {
        var options = Valid();
        options.BaseUrl = "example-site.chargify.com";

        Assert.Contains(options.Validate(), f => f.Contains("Maxio:BaseUrl"));
    }

    [Fact]
    public void RejectsAPaymentCollectionMethodOutsideTheSpecificationEnum()
    {
        var options = Valid();
        options.PaymentCollectionMethod = "carrier-pigeon";

        Assert.Contains(options.Validate(), f => f.Contains("Maxio:PaymentCollectionMethod"));
    }

    [Theory]
    [InlineData("automatic")]
    [InlineData("remittance")]
    [InlineData("prepaid")]
    [InlineData("invoice")]
    public void AcceptsEveryCollectionMethodTheSpecificationDefines(string method)
    {
        var options = Valid();
        options.PaymentCollectionMethod = method;

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void RejectsANonPositiveTimeout()
    {
        var options = Valid();
        options.Timeout = TimeSpan.Zero;

        Assert.Contains(options.Validate(), f => f.Contains("Maxio:Timeout"));
    }

    [Fact]
    public void HasNoBakedInSiteOrCatalogDefaults()
    {
        var options = new MaxioOptions();

        Assert.Null(options.ApiKey);
        Assert.Null(options.Subdomain);
        Assert.Null(options.ProductFamilyHandle);
        Assert.Null(options.BaseUrl);
        Assert.False(options.Validate().Any(f => f.Contains("chargify")));
    }
}
