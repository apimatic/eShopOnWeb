using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Exposes the configured PayPal currency to the application layer.</summary>
public class PayPalCurrencyProvider : IPaymentCurrencyProvider
{
    public PayPalCurrencyProvider(IOptions<PayPalSettings> settings)
    {
        Currency = settings.Value.Currency;
    }

    public string Currency { get; }
}
