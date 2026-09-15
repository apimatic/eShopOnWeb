using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentSettings : IPaymentSettings
{
    private readonly PayPalOptions _options;

    public PayPalPaymentSettings(IOptions<PayPalOptions> options)
    {
        _options = options.Value;
    }

    public string Currency => string.IsNullOrWhiteSpace(_options.Currency)
        ? string.Empty
        : _options.Currency.Trim().ToUpperInvariant();
}
