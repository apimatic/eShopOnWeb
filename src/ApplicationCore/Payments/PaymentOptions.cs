namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Application-level payment settings the core services need, kept provider-agnostic so
/// ApplicationCore does not depend on the PayPal SDK or its Infrastructure settings type.
/// Populated at startup from the bound <c>PayPal:</c> configuration.
/// </summary>
public class PaymentOptions
{
    /// <summary>ISO-4217 currency code for all payments, from <c>PayPal:Currency</c>.</summary>
    public string CurrencyCode { get; set; } = "USD";
}
