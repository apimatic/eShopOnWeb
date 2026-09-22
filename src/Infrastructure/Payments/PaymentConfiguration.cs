using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>Exposes the configured currency to the domain from the bound <see cref="PayPalOptions"/>.</summary>
public class PaymentConfiguration : IPaymentConfiguration
{
    private readonly PayPalOptions _options;

    public PaymentConfiguration(IOptions<PayPalOptions> options) => _options = options.Value;

    public string Currency => _options.Currency;
}
