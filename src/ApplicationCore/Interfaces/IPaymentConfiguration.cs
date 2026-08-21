namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-level view of payment configuration, so ApplicationCore need not reference the
/// Infrastructure settings type. Implemented in Infrastructure from the bound PayPal settings.
/// </summary>
public interface IPaymentConfiguration
{
    /// <summary>ISO-4217 currency code for order amounts (from configuration).</summary>
    string Currency { get; }
}
