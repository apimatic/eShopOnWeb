namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var environment = Environment?.Trim();
        if (string.Equals(environment, "live", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "production", System.StringComparison.OrdinalIgnoreCase))
        {
            return "https://api-m.paypal.com";
        }

        return "https://api-m.sandbox.paypal.com";
    }
}

public sealed class PayPalPaymentSettings : ApplicationCore.Payments.IPaymentSettings
{
    private readonly Microsoft.Extensions.Options.IOptions<PayPalOptions> _options;

    public PayPalPaymentSettings(Microsoft.Extensions.Options.IOptions<PayPalOptions> options)
    {
        _options = options;
    }

    public string Currency
    {
        get
        {
            var currency = _options.Value.Currency;
            if (string.IsNullOrWhiteSpace(currency))
            {
                throw new ApplicationCore.Exceptions.PaymentException(
                    "PayPal:Currency is not configured. Set PAYPAL_CURRENCY or the PayPal:Currency setting.",
                    500,
                    "PAYPAL_NOT_CONFIGURED");
            }

            return currency.Trim().ToUpperInvariant();
        }
    }
}
