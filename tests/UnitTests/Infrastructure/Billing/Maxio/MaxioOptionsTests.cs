using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioOptionsTests
{
    private static MaxioOptions Valid() => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "demo-family",
    };

    [Fact]
    public void DerivesTheUsBaseAddressFromTheSubdomain()
    {
        Assert.Equal("https://acme.chargify.com", Valid().ResolveBaseAddress());
    }

    [Fact]
    public void DerivesTheEuBaseAddressFromTheSubdomain()
    {
        var options = Valid();
        options.Environment = "EU";

        Assert.Equal("https://acme.ebilling.maxio.com", options.ResolveBaseAddress());
    }

    [Theory]
    [InlineData("us")]
    [InlineData("Eu")]
    public void MatchesTheEnvironmentCaseInsensitively(string environment)
    {
        var options = Valid();
        options.Environment = environment;

        var expectedHost = environment.ToUpperInvariant() == "EU" ? "ebilling.maxio.com" : "chargify.com";
        Assert.Contains(expectedHost, options.ResolveBaseAddress());
    }

    [Fact]
    public void UsesBaseUrlVerbatimWhenSet()
    {
        var options = Valid();
        options.BaseUrl = "https://maxio-proxy.internal:8443/advanced-billing";

        Assert.Equal("https://maxio-proxy.internal:8443/advanced-billing", options.ResolveBaseAddress());
    }

    [Fact]
    public void BaseUrlOverridesTheSubdomainAndEnvironment()
    {
        var options = Valid();
        options.Subdomain = "ignored";
        options.Environment = "EU";
        options.BaseUrl = "https://acme.chargify.com";

        Assert.Equal("https://acme.chargify.com", options.ResolveBaseAddress());
    }

    [Fact]
    public void TrimsATrailingSlashFromBaseUrlSoRequestPathsAppendCleanly()
    {
        var options = Valid();
        options.BaseUrl = "https://acme.chargify.com/";

        Assert.Equal("https://acme.chargify.com", options.ResolveBaseAddress());
    }

    [Fact]
    public void BaseUrlSatisfiesTheSubdomainRequirement()
    {
        var options = Valid();
        options.Subdomain = null;
        options.BaseUrl = "https://acme.chargify.com";

        Assert.Equal("https://acme.chargify.com", options.ResolveBaseAddress());
    }

    [Fact]
    public void NamesEveryMissingKeyInOneMessage()
    {
        var options = new MaxioOptions();

        var exception = Assert.Throws<BillingConfigurationException>(() => options.Validate());

        Assert.Contains("Maxio:ApiKey", exception.Message);
        Assert.Contains("Maxio:Subdomain", exception.Message);
        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }

    [Fact]
    public void RejectsARelativeBaseUrl()
    {
        var options = Valid();
        options.BaseUrl = "/advanced-billing";

        var exception = Assert.Throws<BillingConfigurationException>(() => options.Validate());
        Assert.Contains("Maxio:BaseUrl", exception.Message);
    }

    [Fact]
    public void RejectsAnUnknownEnvironment()
    {
        var options = Valid();
        options.Environment = "APAC";

        var exception = Assert.Throws<BillingConfigurationException>(() => options.Validate());
        Assert.Contains("Maxio:Environment", exception.Message);
    }

    [Fact]
    public void RejectsAnUnknownPaymentCollectionMethod()
    {
        var options = Valid();
        options.PaymentCollectionMethod = "cash";

        var exception = Assert.Throws<BillingConfigurationException>(() => options.Validate());
        Assert.Contains("Maxio:PaymentCollectionMethod", exception.Message);
    }

    [Theory]
    [InlineData("automatic")]
    [InlineData("remittance")]
    [InlineData("invoice")]
    [InlineData("prepaid")]
    [InlineData("Remittance")]
    public void AcceptsEveryCollectionMethodMaxioSupports(string method)
    {
        Assert.True(MaxioOptions.IsSupportedCollectionMethod(method));
    }

    [Theory]
    [InlineData("cash")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsCollectionMethodsMaxioDoesNotSupport(string? method)
    {
        Assert.False(MaxioOptions.IsSupportedCollectionMethod(method));
    }

    [Fact]
    public void DefaultsToRemittanceSoSubscribingNeedsNoStoredPaymentMethod()
    {
        Assert.Equal("remittance", new MaxioOptions().PaymentCollectionMethod);
    }
}
