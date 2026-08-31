namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Payment behaviour settings. Bound from the PayPal configuration section;
/// only the currency is needed by the domain services.
/// </summary>
public class PaymentSettings
{
    public string Currency { get; set; } = "USD";
}
