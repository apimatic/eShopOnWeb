namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Payment settings the application layer needs — currently the currency all amounts are denominated
/// in. Bound from the <c>PayPal:Currency</c> configuration key by the host.
/// </summary>
public class PaymentSettings
{
    public string CurrencyCode { get; set; } = "USD";
}
