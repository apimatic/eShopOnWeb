using System.Net.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public sealed class PayPalSettings : IPayPalSettings
{
    public PayPalSettings(IOptions<PayPalOptions> options)
    {
        Currency = options.Value.Currency;
    }

    public string Currency { get; }
}
