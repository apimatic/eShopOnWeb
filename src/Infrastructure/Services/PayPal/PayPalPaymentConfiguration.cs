using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>Exposes the configured currency to the application layer without leaking PayPalSettings.</summary>
public class PayPalPaymentConfiguration : IPaymentConfiguration
{
    public PayPalPaymentConfiguration(IOptions<PayPalSettings> settings)
    {
        Currency = settings.Value.Currency;
    }

    public string Currency { get; }
}
