using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>Adapts <see cref="PayPalOptions"/> to the application-core payment configuration abstraction.</summary>
public class PaymentConfiguration : IPaymentConfiguration
{
    private readonly PayPalOptions _options;

    public PaymentConfiguration(IOptions<PayPalOptions> options)
    {
        _options = options.Value;
    }

    public string Currency => _options.Currency;
}
