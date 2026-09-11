using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Exposes the configured PayPal currency to the application core.</summary>
public class PayPalCurrencyProvider : ICurrencyProvider
{
    public PayPalCurrencyProvider(PayPalOptions options)
    {
        CurrencyCode = options.Currency;
    }

    public string CurrencyCode { get; }
}
