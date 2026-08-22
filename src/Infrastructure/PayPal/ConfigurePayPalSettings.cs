using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class ConfigurePayPalSettings : IPayPalConfiguration
{
    private readonly PayPalSettings _settings;

    public ConfigurePayPalSettings(IOptions<PayPalSettings> options)
    {
        _settings = options.Value;
    }

    public string Currency => _settings.Currency;
    public string ClientId => _settings.ClientId;
    public string ClientSecret => _settings.ClientSecret;
    public string Environment => _settings.Environment;
    public string? BaseUrl => _settings.BaseUrl;
}
