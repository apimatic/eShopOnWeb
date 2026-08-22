using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments.PayPal;

public sealed class PayPalPaymentSettings : IPaymentSettings
{
    public PayPalPaymentSettings(IOptions<PayPalOptions> options)
    {
        Currency = string.IsNullOrWhiteSpace(options.Value.Currency) ? "USD" : options.Value.Currency;
    }

    public string Currency { get; }
}
