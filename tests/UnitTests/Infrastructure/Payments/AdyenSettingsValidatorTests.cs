using Microsoft.eShopWeb.Infrastructure.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

public class AdyenSettingsValidatorTests
{
    private static AdyenSettings Valid() => new()
    {
        ApiKey = "offline-secret-value",
        MerchantAccount = "OfflineTestMerchant",
        Environment = "test",
        Currency = "USD",
        ReturnUrl = "https://shop.example/Order/MyOrders",
    };

    [Fact]
    public void AcceptsCompleteTestSettings()
    {
        Assert.True(new AdyenSettingsValidator().Validate(null, Valid()).Succeeded);
    }

    [Fact]
    public void NamesEveryMissingKeyWithoutEchoingValues()
    {
        var settings = new AdyenSettings { ApiKey = " ", ReturnUrl = "https://shop.example/" };

        var result = new AdyenSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.StartsWith("Adyen:ApiKey is not configured"));
        Assert.Contains(result.Failures!, f => f.StartsWith("Adyen:MerchantAccount is not configured"));
        Assert.Contains(result.Failures!, f => f.StartsWith("Adyen:Environment is not configured"));
        Assert.Contains(result.Failures!, f => f.StartsWith("Adyen:Currency is not configured"));
    }

    [Fact]
    public void NeverEchoesTheApiKey()
    {
        var settings = Valid();
        settings.Environment = "live";

        var result = new AdyenSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.DoesNotContain("offline-secret-value", result.FailureMessage);
    }

    [Theory]
    [InlineData("live")]
    [InlineData("production")]
    public void RefusesAnythingButTheTestEnvironment(string environment)
    {
        var settings = Valid();
        settings.Environment = environment;

        var result = new AdyenSettingsValidator().Validate(null, settings);

        Assert.Contains(result.Failures!, f => f.StartsWith("Adyen:Environment must be 'test'"));
    }

    [Theory]
    [InlineData("US")]
    [InlineData("US1")]
    [InlineData("DOLLAR")]
    public void RefusesANonIsoCurrency(string currency)
    {
        var settings = Valid();
        settings.Currency = currency;

        Assert.Contains(new AdyenSettingsValidator().Validate(null, settings).Failures!, f => f.StartsWith("Adyen:Currency must be"));
    }
}
