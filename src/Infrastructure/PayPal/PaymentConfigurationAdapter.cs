using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Exposes the configured PayPal currency to ApplicationCore without leaking the settings type.</summary>
public class PaymentConfigurationAdapter : IPaymentConfiguration
{
    private readonly PayPalSettings _settings;

    public PaymentConfigurationAdapter(IOptions<PayPalSettings> settings)
    {
        _settings = settings.Value;
    }

    public string Currency => string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency;
}
