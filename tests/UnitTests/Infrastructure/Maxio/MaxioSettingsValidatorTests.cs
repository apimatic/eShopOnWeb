using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsValidatorTests
{
    private readonly MaxioSettingsValidator _validator = new();

    private static MaxioSettings Valid() => new()
    {
        ApiKey = "an-api-key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    [Fact]
    public void AcceptsAFullyConfiguredSection()
    {
        Assert.True(_validator.Validate(name: null, Valid()).Succeeded);
    }

    [Fact]
    public void RequiresAnApiKey()
    {
        var settings = Valid();
        settings.ApiKey = "";

        var result = _validator.Validate(name: null, settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Maxio:ApiKey"));
    }

    [Fact]
    public void RequiresASubdomainUnlessBaseUrlIsSet()
    {
        var settings = Valid();
        settings.Subdomain = "";

        Assert.True(_validator.Validate(name: null, settings).Failed);

        settings.BaseUrl = "https://acme.ebilling.maxio.com";
        Assert.True(_validator.Validate(name: null, settings).Succeeded);
    }

    [Fact]
    public void RequiresAProductFamilyHandle()
    {
        var settings = Valid();
        settings.ProductFamilyHandle = " ";

        var result = _validator.Validate(name: null, settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Maxio:ProductFamilyHandle"));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://acme.chargify.com")]
    public void RejectsABaseUrlThatIsNotAnAbsoluteHttpUrl(string baseUrl)
    {
        var settings = Valid();
        settings.BaseUrl = baseUrl;

        var result = _validator.Validate(name: null, settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Maxio:BaseUrl"));
    }

    [Fact]
    public void RejectsAnUnknownPaymentCollectionMethod()
    {
        var settings = Valid();
        settings.PaymentCollectionMethod = "cheque";

        var result = _validator.Validate(name: null, settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Maxio:PaymentCollectionMethod"));
    }

    [Fact]
    public void ReportsEveryProblemAtOnce()
    {
        var settings = new MaxioSettings { PaymentCollectionMethod = "cheque" };

        var result = _validator.Validate(name: null, settings);

        Assert.True(result.Failed);
        Assert.Equal(4, result.Failures.Count());
    }
}
