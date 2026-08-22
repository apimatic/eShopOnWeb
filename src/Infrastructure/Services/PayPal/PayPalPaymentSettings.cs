using System;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public class PayPalPaymentSettings : IPaymentSettings
{
    private readonly PayPalOptions _options;

    public PayPalPaymentSettings(IOptions<PayPalOptions> options)
    {
        _options = options.Value;
    }

    public string Currency => _options.Currency ?? string.Empty;
}
