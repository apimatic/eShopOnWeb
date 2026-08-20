using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalPaymentSettings : IPaymentSettings
{
    public PayPalPaymentSettings(IOptions<PayPalOptions> options)
    {
        Currency = string.IsNullOrWhiteSpace(options.Value.Currency) ? "USD" : options.Value.Currency;
    }

    public string Currency { get; }
}
