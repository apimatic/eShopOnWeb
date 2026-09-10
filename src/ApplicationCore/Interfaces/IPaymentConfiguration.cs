namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Payment-related configuration the application layer needs. The currency comes from
/// configuration (<c>PayPal:Currency</c>) rather than being hard-coded.
/// </summary>
public interface IPaymentConfiguration
{
    /// <summary>ISO-4217 currency code used for all order amounts.</summary>
    string Currency { get; }
}
