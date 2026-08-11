using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Exposes the configured currency to the application layer without leaking configuration/HTTP concerns.</summary>
public class PayPalConfiguration : IPaymentConfiguration
{
    private readonly PayPalOptions _options;

    public PayPalConfiguration(PayPalOptions options)
    {
        _options = options;
    }

    public string Currency => _options.ResolveCurrency();
}
