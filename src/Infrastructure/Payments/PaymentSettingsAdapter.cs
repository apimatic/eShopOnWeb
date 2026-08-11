using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>Exposes the currency from <see cref="PayPalSettings"/> to the application layer.</summary>
public class PaymentSettingsAdapter : IPaymentSettings
{
    private readonly PayPalSettings _settings;

    public PaymentSettingsAdapter(IOptions<PayPalSettings> options) => _settings = options.Value;

    public string Currency => _settings.Currency;
}
