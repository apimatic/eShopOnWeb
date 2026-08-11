namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-level payment settings resolved from configuration (the <c>PayPal:</c> section).
/// Kept as an abstraction so the domain services do not depend on the configuration provider.
/// </summary>
public interface IPaymentSettings
{
    /// <summary>ISO-4217 currency code all amounts are charged in (from <c>PayPal:Currency</c>).</summary>
    string CurrencyCode { get; }
}
