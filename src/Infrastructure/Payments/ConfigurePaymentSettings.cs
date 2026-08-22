using System.Net.Http;
using System.Threading;
using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class ConfigurePaymentSettings : IPaymentSettings
{
    private readonly PayPalOptions _options;

    public ConfigurePaymentSettings(IOptions<PayPalOptions> options)
    {
        _options = options.Value;
    }

    public string Currency
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.Currency))
            {
                throw new InvalidOperationException("PayPal:Currency is not configured.");
            }

            return _options.Currency;
        }
    }
}
