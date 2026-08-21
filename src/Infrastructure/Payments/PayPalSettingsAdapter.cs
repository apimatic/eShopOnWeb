using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalSettingsAdapter : IPayPalSettings
{
    private readonly PayPalOptions _options;

    public PayPalSettingsAdapter(IOptions<PayPalOptions> options)
    {
        _options = options.Value;
    }

    public string Currency => _options.Currency?.Trim() ?? string.Empty;
    public string Environment => _options.Environment?.Trim() ?? string.Empty;
}
