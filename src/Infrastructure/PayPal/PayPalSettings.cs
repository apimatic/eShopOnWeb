using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Exposes the configured currency to the application layer without leaking the options plumbing.</summary>
public class PayPalSettings : IPaymentSettings
{
    private readonly PayPalOptions _options;

    public PayPalSettings(IOptions<PayPalOptions> options)
    {
        _options = options.Value;
    }

    public string CurrencyCode => _options.Currency;
}
