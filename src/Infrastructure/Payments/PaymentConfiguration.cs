using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PaymentConfiguration : IPaymentConfiguration
{
    public PaymentConfiguration(IOptions<PayPalSettings> options)
    {
        Currency = options.Value.Currency ?? string.Empty;
    }

    public string Currency { get; }
}
