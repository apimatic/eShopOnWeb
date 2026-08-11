namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Payment settings the application layer needs. Implemented in Infrastructure by binding the PayPal
/// configuration section, so ApplicationCore stays free of configuration/HTTP concerns.
/// </summary>
public interface IPaymentConfiguration
{
    /// <summary>ISO-4217 currency code the integration transacts in (from PayPal:Currency).</summary>
    string Currency { get; }
}
