using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Exposes the configured currency to the application layer without leaking the Infrastructure settings type.</summary>
public class PayPalPaymentSettings : IPaymentSettings
{
    private readonly PayPalSettings _settings;

    public PayPalPaymentSettings(IOptions<PayPalSettings> settings)
    {
        _settings = settings.Value;
    }

    public string Currency => _settings.Currency;
}
