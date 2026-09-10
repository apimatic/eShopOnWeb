using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Supplies the payment currency from <c>PayPal:Currency</c> configuration.</summary>
public class ConfiguredCurrencyProvider : ICurrencyProvider
{
    public ConfiguredCurrencyProvider(IOptions<PayPalOptions> options)
    {
        CurrencyCode = options.Value.Currency;
    }

    public string CurrencyCode { get; }
}
