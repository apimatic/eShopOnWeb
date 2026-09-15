namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Application-level payment settings needed by the domain services (as opposed to the
/// PayPal transport credentials, which live in the Infrastructure layer). Bound from the
/// <c>PayPal:</c> configuration section.
/// </summary>
public class PaymentSettings
{
    /// <summary>ISO-4217 currency code all amounts are charged in (from <c>PayPal:Currency</c>).</summary>
    public string Currency { get; set; } = "USD";
}
