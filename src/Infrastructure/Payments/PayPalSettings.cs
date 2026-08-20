using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalSettings : IPayPalSettings
{
    private readonly PayPalOptions _options;

    public PayPalSettings(IOptions<PayPalOptions> options)
    {
        _options = options.Value;
    }

    public string Currency => _options.Currency;
    public string ClientId => _options.ClientId;
    public string ClientSecret => _options.ClientSecret;
    public string Environment => _options.Environment;
    public string? BaseUrl => string.IsNullOrWhiteSpace(_options.BaseUrl) ? null : _options.BaseUrl;
}
