using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioOptionsTests
{
    private static MaxioOptions Valid() => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "plans"
    };

    [Fact]
    public void DerivesTheBaseAddressFromTheSubdomain()
    {
        var options = Valid();

        Assert.Equal("https://acme.chargify.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenSet()
    {
        var options = Valid();
        options.BaseUrl = "https://acme.ebilling.maxio.com/";

        Assert.Equal("https://acme.ebilling.maxio.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void AppendsATrailingSlashSoRelativePathsResolveUnderTheOverride()
    {
        var options = Valid();
        options.BaseUrl = "https://gateway.example.com/api/v1/billing";

        var baseAddress = options.ResolveBaseAddress();

        Assert.Equal("https://gateway.example.com/api/v1/billing/", baseAddress.ToString());
        Assert.Equal("https://gateway.example.com/api/v1/billing/subscriptions.json",
            new Uri(baseAddress, "subscriptions.json").ToString());
    }

    [Fact]
    public void BaseUrlRemovesTheNeedForASubdomain()
    {
        var options = Valid();
        options.Subdomain = null;
        options.BaseUrl = "https://acme.chargify.com/";

        options.EnsureValid();
    }

    [Fact]
    public void RejectsAMissingApiKey()
    {
        var options = Valid();
        options.ApiKey = "  ";

        var exception = Assert.Throws<BillingConfigurationException>(() => options.EnsureValid());
        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    [Fact]
    public void RejectsAMissingProductFamilyHandle()
    {
        var options = Valid();
        options.ProductFamilyHandle = null;

        var exception = Assert.Throws<BillingConfigurationException>(() => options.EnsureValid());
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }

    [Fact]
    public void RejectsAMissingSiteWhenNoBaseUrlIsGiven()
    {
        var options = Valid();
        options.Subdomain = null;

        var exception = Assert.Throws<BillingConfigurationException>(() => options.EnsureValid());
        Assert.Contains("Maxio:Subdomain", exception.Message);
    }

    [Fact]
    public void RejectsARelativeBaseUrl()
    {
        var options = Valid();
        options.BaseUrl = "not-a-url";

        Assert.Throws<BillingConfigurationException>(() => options.ResolveBaseAddress());
    }
}
