using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Supplies the application layer with the charge currency, bound from PayPal:Currency at the host.
/// Never hard-coded — the value flows from configuration/secrets.
/// </summary>
public sealed class ConfiguredPaymentSettings : IPaymentSettings
{
    public ConfiguredPaymentSettings(string currency) => Currency = currency;

    public string Currency { get; }
}
