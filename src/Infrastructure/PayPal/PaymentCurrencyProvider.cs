using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PaymentCurrencyProvider : IPaymentCurrencyProvider
{
    public PaymentCurrencyProvider(IOptions<PayPalSettings> settings)
    {
        Currency = settings.Value.Currency;
    }

    public string Currency { get; }
}
