using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioSettingsTests
{
    private static MaxioSettings Valid() => new()
    {
        ApiKey = "key",
        Subdomain = "acme",
        ProductFamilyHandle = "plans",
    };

    private static string[] Validate(MaxioSettings settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results.Select(r => r.ErrorMessage!).ToArray();
    }

    [Fact]
    public void DerivesTheUsBaseAddressFromTheSubdomain()
    {
        Assert.Equal("https://acme.chargify.com/", Valid().ResolveBaseAddress().ToString());
    }

    [Fact]
    public void DerivesTheEuBaseAddressFromTheSubdomain()
    {
        var settings = Valid();
        settings.Environment = "EU";

        Assert.Equal("https://acme.ebilling.maxio.com/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void UsesBaseUrlVerbatimInPreferenceToTheSubdomain()
    {
        var settings = Valid();
        settings.BaseUrl = "https://gateway.internal/billing/";

        Assert.Equal("https://gateway.internal/billing/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void AppendsATrailingSlashSoPathPrefixesSurviveComposition()
    {
        var settings = Valid();
        settings.BaseUrl = "https://gateway.internal/billing";

        Assert.Equal("https://gateway.internal/billing/", settings.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void AcceptsAValidConfiguration()
    {
        Assert.Empty(Validate(Valid()));
    }

    [Fact]
    public void RequiresAnApiKey()
    {
        var settings = Valid();
        settings.ApiKey = "   ";

        Assert.Contains(Validate(settings), m => m.Contains("Maxio:ApiKey"));
    }

    [Fact]
    public void NeverEchoesTheApiKeyInAValidationMessage()
    {
        var settings = Valid();
        settings.ApiKey = "super-secret-key";
        settings.ProductFamilyHandle = null;

        Assert.DoesNotContain(Validate(settings), m => m.Contains("super-secret-key"));
    }

    [Fact]
    public void RequiresAProductFamilyHandle()
    {
        var settings = Valid();
        settings.ProductFamilyHandle = null;

        Assert.Contains(Validate(settings), m => m.Contains("Maxio:ProductFamilyHandle"));
    }

    [Fact]
    public void RequiresASubdomainWhenNoBaseUrlIsGiven()
    {
        var settings = Valid();
        settings.Subdomain = null;

        Assert.Contains(Validate(settings), m => m.Contains("Maxio:Subdomain"));
    }

    [Fact]
    public void AllowsAMissingSubdomainWhenBaseUrlIsGiven()
    {
        var settings = Valid();
        settings.Subdomain = null;
        settings.BaseUrl = "https://gateway.internal/";

        Assert.Empty(Validate(settings));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("/relative/only")]
    [InlineData("ftp://gateway.internal/")]
    public void RejectsABaseUrlThatIsNotAnAbsoluteHttpUrl(string baseUrl)
    {
        var settings = Valid();
        settings.BaseUrl = baseUrl;

        Assert.Contains(Validate(settings), m => m.Contains("Maxio:BaseUrl"));
    }

    [Fact]
    public void RejectsAnUnknownEnvironment()
    {
        var settings = Valid();
        settings.Environment = "APAC";

        Assert.Contains(Validate(settings), m => m.Contains("Maxio:Environment"));
    }

    [Fact]
    public void RejectsACollectionMethodAdvancedBillingDoesNotAccept()
    {
        var settings = Valid();
        settings.PaymentCollectionMethod = "cheque";

        Assert.Contains(Validate(settings), m => m.Contains("Maxio:PaymentCollectionMethod"));
    }

    [Theory]
    [InlineData("remittance")]
    [InlineData("automatic")]
    [InlineData("prepaid")]
    [InlineData("invoice")]
    [InlineData("REMITTANCE")]
    public void AcceptsEveryCollectionMethodAdvancedBillingDocuments(string method)
    {
        var settings = Valid();
        settings.PaymentCollectionMethod = method;

        Assert.Empty(Validate(settings));
    }

    [Fact]
    public void DefaultsToACollectionMethodThatDoesNotNeedAPaymentMethodOnFile()
    {
        Assert.Equal("remittance", new MaxioSettings().PaymentCollectionMethod);
    }
}
