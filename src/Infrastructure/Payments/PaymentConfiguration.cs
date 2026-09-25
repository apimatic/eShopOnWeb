using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>Exposes payment configuration (currency) to the application layer from <see cref="PayPalSettings"/>.</summary>
public sealed class PaymentConfiguration : IPaymentConfiguration
{
    private readonly IOptions<PayPalSettings> _settings;

    public PaymentConfiguration(IOptions<PayPalSettings> settings) => _settings = settings;

    public string CurrencyCode => _settings.Value.Currency;
}
