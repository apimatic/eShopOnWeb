using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public sealed class PayPalPaymentConfiguration : IPaymentConfiguration
{
    private readonly IOptionsMonitor<PayPalOptions> _options;

    public PayPalPaymentConfiguration(IOptionsMonitor<PayPalOptions> options)
    {
        _options = options;
    }

    public string Currency => _options.CurrentValue.Currency ?? string.Empty;
}
