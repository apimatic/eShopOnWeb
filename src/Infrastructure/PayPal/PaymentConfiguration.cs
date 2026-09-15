using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>Exposes the configured PayPal currency to the domain via <see cref="IPaymentConfiguration"/>.</summary>
public class PaymentConfiguration : IPaymentConfiguration
{
    private readonly PayPalSettings _settings;

    public PaymentConfiguration(IOptions<PayPalSettings> settings)
    {
        _settings = settings.Value;
    }

    public string Currency => _settings.ResolveCurrency();
}
