using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Exposes the configured <c>PayPal:Currency</c> to the application layer.</summary>
public class PayPalCurrencyProvider : IPaymentCurrencyProvider
{
    private readonly PayPalSettings _settings;

    public PayPalCurrencyProvider(IOptions<PayPalSettings> settings) => _settings = settings.Value;

    public string CurrencyCode =>
        string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency.Trim().ToUpperInvariant();
}
