using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class ConfigurePayPalPaymentSettings : IPaymentSettings
{
    private readonly PayPalSettings _settings;

    public ConfigurePayPalPaymentSettings(IOptions<PayPalSettings> options)
    {
        _settings = options.Value;
    }

    public string Currency => _settings.Currency;
}
