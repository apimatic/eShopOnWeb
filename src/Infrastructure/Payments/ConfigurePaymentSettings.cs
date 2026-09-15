using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class ConfigurePaymentSettings : IPaymentSettings
{
    public ConfigurePaymentSettings(IOptions<PayPalOptions> options)
    {
        Currency = options.Value.Currency;
    }

    public string Currency { get; }
}
