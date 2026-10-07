using Microsoft.eShopWeb.Infrastructure.Payments.Adyen;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Adyen;

public class AdyenSettingsValidatorTests
{
    private static AdyenSettings Valid() => new()
    {
        ApiKey = "key-value-that-must-never-be-echoed",
        MerchantAccount = "Merchant",
        Environment = "test",
        Currency = "USD",
        ReturnUrl = "https://shop.example.com/order/my-orders",
    };

    [Fact]
    public void AcceptsCompleteSettings()
    {
        Assert.True(new AdyenSettingsValidator().Validate(null, Valid()).Succeeded);
    }

    [Theory]
    [InlineData(nameof(AdyenSettings.ApiKey), "Adyen:ApiKey")]
    [InlineData(nameof(AdyenSettings.MerchantAccount), "Adyen:MerchantAccount")]
    [InlineData(nameof(AdyenSettings.Environment), "Adyen:Environment")]
    [InlineData(nameof(AdyenSettings.Currency), "Adyen:Currency")]
    public void RefusesBlankValuesNamingTheKey(string property, string key)
    {
        var settings = Valid();
        typeof(AdyenSettings).GetProperty(property)!.SetValue(settings, "  ");

        var result = new AdyenSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains(key, result.FailureMessage);
        Assert.DoesNotContain("key-value-that-must-never-be-echoed", result.FailureMessage);
    }

    [Fact]
    public void RefusesAnEnvironmentTheSdkCannotReach()
    {
        var settings = Valid();
        settings.Environment = "live";

        Assert.True(new AdyenSettingsValidator().Validate(null, settings).Failed);
    }

    [Fact]
    public void RefusesAnUnknownCurrency()
    {
        var settings = Valid();
        settings.Currency = "ABC";

        Assert.True(new AdyenSettingsValidator().Validate(null, settings).Failed);
    }
}
