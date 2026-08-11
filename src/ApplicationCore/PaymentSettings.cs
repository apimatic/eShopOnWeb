namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings the payment flow needs but that are not PayPal-transport concerns.
/// The currency is bound from configuration (PayPal:Currency) and is never hard-coded.
/// </summary>
public class PaymentSettings
{
    public string Currency { get; set; } = "USD";
}
