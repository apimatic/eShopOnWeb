using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Exposes the configured currency to the domain without leaking the full options type.</summary>
public class PayPalConfiguration : IPaymentConfiguration
{
    public PayPalConfiguration(IOptions<PayPalOptions> options)
    {
        Currency = options.Value.Currency;
    }

    public string Currency { get; }
}
