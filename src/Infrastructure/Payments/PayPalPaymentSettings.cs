using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalPaymentSettings : IPaymentSettings
{
    private readonly PayPalOptions _options;

    public PayPalPaymentSettings(IOptions<PayPalOptions> options)
    {
        _options = options.Value;
    }

    public string Currency => string.IsNullOrWhiteSpace(_options.Currency) ? "USD" : _options.Currency;
    public string Environment => _options.Environment;
    public string BaseUrl => _options.ResolveBaseUrl();
}
