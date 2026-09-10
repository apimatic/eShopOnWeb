namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Payment settings the domain needs, bound from the <c>PayPal:</c> configuration section.
/// </summary>
public class PaymentSettings
{
    /// <summary>ISO-4217 currency code all payments are taken in (from PayPal:Currency).</summary>
    public string CurrencyCode { get; set; } = "USD";
}
