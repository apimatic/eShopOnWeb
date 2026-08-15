namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Payment configuration the application layer needs. Bound from the "PayPal" configuration
/// section at the host; the currency for all charges comes from here, never hard-coded.
/// </summary>
public interface IPaymentSettings
{
    /// <summary>ISO-4217 currency code used for every charge (from PayPal:Currency).</summary>
    string Currency { get; }
}
