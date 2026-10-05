using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PaymentsTests.Gateway;

public class PayPalOptionsTests
{
    private static ValidateOptionsResult Validate(PayPalOptions options) => new PayPalOptionsValidator().Validate(null, options);

    private static PayPalOptions Valid() => new()
    {
        ClientId = "id",
        ClientSecret = "secret",
        Environment = "sandbox",
        Currency = "USD",
    };

    [Fact]
    public void A_complete_sandbox_configuration_is_valid() => Assert.True(Validate(Valid()).Succeeded);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_or_blank_secret_fails_naming_the_key_but_not_any_value(string? secret)
    {
        var options = Valid();
        options.ClientSecret = secret;

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("PayPal:ClientSecret", result.FailureMessage);
        Assert.DoesNotContain("secret\"", result.FailureMessage);
    }

    [Fact]
    public void A_missing_client_id_fails() => Assert.True(Validate(new PayPalOptions { ClientSecret = "s", Environment = "sandbox", Currency = "USD" }).Failed);

    [Theory]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("1SD")]
    public void Currency_must_be_an_ISO_code(string currency)
    {
        var options = Valid();
        options.Currency = currency;
        Assert.True(Validate(options).Failed);
    }

    [Fact]
    public void A_non_sandbox_environment_needs_an_explicit_base_url()
    {
        var options = Valid();
        options.Environment = "live";
        Assert.True(Validate(options).Failed);

        options.BaseUrl = "https://api.example.internal";
        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Host_refuses_to_resolve_options_when_credentials_are_missing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PayPal:Environment"] = "sandbox",
            ["PayPal:Currency"] = "USD",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPayPalPayments(configuration);
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<PayPalOptions>>().Value);
        Assert.Contains("PayPal:ClientId", ex.Message);
    }
}
