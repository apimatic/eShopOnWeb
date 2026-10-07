using Microsoft.eShopWeb.Infrastructure.Payments.Adyen;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

public class AdyenSettingsValidatorTests
{
    private static AdyenSettings Valid() => new() { ApiKey = "secret-value", MerchantAccount = "Merchant", Environment = "test", Currency = "USD" };

    [Fact]
    public void ValidSettings_Pass()
    {
        Assert.True(new AdyenSettingsValidator().Validate(null, Valid()).Succeeded);
    }

    [Theory]
    [InlineData("", "Adyen:ApiKey")]
    [InlineData("   ", "Adyen:ApiKey")]
    public void MissingApiKey_FailsNamingTheKey(string apiKey, string expectedKey)
    {
        var settings = Valid();
        settings.ApiKey = apiKey;

        var result = new AdyenSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains(expectedKey, result.FailureMessage);
    }

    [Fact]
    public void Failures_NeverEchoTheApiKey()
    {
        var settings = Valid();
        settings.MerchantAccount = "";

        var result = new AdyenSettingsValidator().Validate(null, settings);

        Assert.Contains("Adyen:MerchantAccount", result.FailureMessage);
        Assert.DoesNotContain("secret-value", result.FailureMessage);
        Assert.DoesNotContain("secret-value", settings.ToString());
    }

    [Theory]
    [InlineData("live")]
    [InlineData("")]
    public void NonTestEnvironment_Fails(string environment)
    {
        var settings = Valid();
        settings.Environment = environment;

        Assert.Contains("Adyen:Environment", new AdyenSettingsValidator().Validate(null, settings).FailureMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("US1")]
    public void InvalidCurrency_Fails(string currency)
    {
        var settings = Valid();
        settings.Currency = currency;

        Assert.Contains("Adyen:Currency", new AdyenSettingsValidator().Validate(null, settings).FailureMessage);
    }
}
