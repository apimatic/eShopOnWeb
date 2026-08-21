using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Exposes just the currency the application layer needs, sourced from <see cref="PayPalSettings"/>.</summary>
public class PayPalPaymentConfiguration : IPaymentConfiguration
{
    private readonly PayPalSettings _settings;

    public PayPalPaymentConfiguration(IOptions<PayPalSettings> settings)
    {
        _settings = settings.Value;
    }

    public string CurrencyCode => _settings.Currency;
}
