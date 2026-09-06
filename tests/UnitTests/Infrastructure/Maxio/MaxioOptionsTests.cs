using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    private static MaxioOptions Valid() => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "plans"
    };

    [Fact]
    public void DerivesBaseAddressFromSubdomainUsingTheSpecServerTemplate()
    {
        var options = Valid();

        Assert.Equal("https://acme.chargify.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenItIsSet()
    {
        var options = Valid();
        options.BaseUrl = "https://acme.ebilling.maxio.com";

        // The subdomain is ignored entirely, so an EU or proxied host is honoured as written.
        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void DoesNotDoubleTheTrailingSlashOnBaseUrl()
    {
        var options = Valid();
        options.BaseUrl = "https://acme.ebilling.maxio.com/";

        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ResolvesBaseAddressFromBaseUrlWhenNoSubdomainIsConfigured()
    {
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            ProductFamilyHandle = "plans",
            BaseUrl = "https://billing.internal.example/"
        };

        Assert.Empty(options.Validate());
        Assert.Equal("https://billing.internal.example/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ValidatesCleanlyWhenTheRequiredKeysArePresent()
    {
        Assert.Empty(Valid().Validate());
    }

    [Theory]
    [InlineData(null, "acme", "plans", "Maxio:ApiKey")]
    [InlineData("key", null, "plans", "Maxio:Subdomain")]
    [InlineData("key", "acme", null, "Maxio:ProductFamilyHandle")]
    public void ReportsEachMissingRequiredKeyByName(string? apiKey, string? subdomain, string? family, string expectedKey)
    {
        var options = new MaxioOptions
        {
            ApiKey = apiKey,
            Subdomain = subdomain,
            ProductFamilyHandle = family
        };

        Assert.Contains(options.Validate(), failure => failure.Contains(expectedKey));
    }

    [Fact]
    public void RejectsACollectionMethodThatIsNotInTheSpecEnum()
    {
        var options = Valid();
        options.PaymentCollectionMethod = "cheque";

        Assert.Contains(options.Validate(), failure => failure.Contains("PaymentCollectionMethod"));
    }

    [Theory]
    [InlineData("automatic")]
    [InlineData("remittance")]
    [InlineData("prepaid")]
    [InlineData("invoice")]
    public void AcceptsEveryCollectionMethodTheSpecDefines(string method)
    {
        var options = Valid();
        options.PaymentCollectionMethod = method;

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void RejectsAPageSizeAboveTheSpecMaximum()
    {
        var options = Valid();
        options.PageSize = 500;

        Assert.Contains(options.Validate(), failure => failure.Contains("PageSize"));
    }

    [Fact]
    public void RejectsABaseUrlThatIsNotAbsolute()
    {
        var options = Valid();
        options.BaseUrl = "not-a-url";

        Assert.Contains(options.Validate(), failure => failure.Contains("BaseUrl"));
    }
}
