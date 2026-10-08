using System.Linq;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

public class AdyenSettingsValidatorTests
{
    private static AdyenSettings Valid() => new()
    {
        ApiKey = "some-key-value",
        MerchantAccount = "Merchant",
        Environment = "test",
        Currency = "USD"
    };

    [Fact]
    public void Accepts_complete_test_settings()
    {
        Assert.True(new AdyenSettingsValidator().Validate(null, Valid()).Succeeded);
    }

    [Theory]
    [InlineData("ApiKey", "Adyen:ApiKey")]
    [InlineData("MerchantAccount", "Adyen:MerchantAccount")]
    [InlineData("Environment", "Adyen:Environment")]
    [InlineData("Currency", "Adyen:Currency")]
    public void Blank_setting_fails_naming_its_key(string property, string key)
    {
        var settings = Valid();
        typeof(AdyenSettings).GetProperty(property)!.SetValue(settings, "  ");

        var result = new AdyenSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(key));
    }

    [Fact]
    public void Failure_message_never_echoes_the_api_key()
    {
        var settings = Valid();
        settings.MerchantAccount = null;

        var result = new AdyenSettingsValidator().Validate(null, settings);

        Assert.DoesNotContain(result.Failures!, f => f.Contains("some-key-value"));
    }

    [Fact]
    public void Live_without_checkout_base_url_refuses_to_start()
    {
        var settings = Valid();
        settings.Environment = "live";

        var result = new AdyenSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Adyen:CheckoutBaseUrl"));
    }

    [Fact]
    public void Lower_case_currency_is_rejected()
    {
        var settings = Valid();
        settings.Currency = "usd";

        Assert.True(new AdyenSettingsValidator().Validate(null, settings).Failed);
    }
}
