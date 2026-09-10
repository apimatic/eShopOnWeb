namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Supplies the payment currency (from <c>PayPal:Currency</c> configuration). Kept as an
/// abstraction so ApplicationCore does not depend on how settings are bound.
/// </summary>
public interface ICurrencyProvider
{
    /// <summary>ISO-4217 currency code, e.g. USD.</summary>
    string CurrencyCode { get; }
}
