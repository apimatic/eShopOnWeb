namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Payment settings the application layer needs. The currency comes from configuration
/// (<c>PayPal:Currency</c>); it is never hard-coded.
/// </summary>
public interface IPaymentSettings
{
    /// <summary>The three-character ISO-4217 currency code used for all payments.</summary>
    string Currency { get; }
}
