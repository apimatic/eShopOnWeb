using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Exposes the configured currency to the application layer without leaking PayPal settings.</summary>
public class PayPalPaymentConfiguration : IPaymentConfiguration
{
    private readonly PayPalSettings _settings;
    public PayPalPaymentConfiguration(PayPalSettings settings) => _settings = settings;

    public string CurrencyCode => _settings.Currency;
}
