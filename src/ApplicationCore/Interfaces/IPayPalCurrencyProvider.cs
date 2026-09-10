namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Exposes the configured PayPal currency (from the <c>PayPal:Currency</c> setting) to the domain services
/// without making ApplicationCore depend on the options/configuration types.
/// </summary>
public interface IPayPalCurrencyProvider
{
    string Currency { get; }
}
