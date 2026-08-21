using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalPaymentSettings : IPaymentSettings
{
    public PayPalPaymentSettings(IOptions<PayPalOptions> options)
    {
        Currency = options.Value.Currency;
    }

    public string Currency { get; }
}
