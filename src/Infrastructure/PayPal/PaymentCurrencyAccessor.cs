using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PaymentCurrencyAccessor : IPaymentCurrencyAccessor
{
    public PaymentCurrencyAccessor(IOptions<PayPalOptions> options)
    {
        var currency = options.Value.Currency;
        Currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
    }

    public string Currency { get; }
}
