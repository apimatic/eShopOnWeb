namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>Payment-related settings the core needs; bound from the PayPal configuration section.</summary>
public class PaymentOptions
{
    public string Currency { get; set; } = "USD";
}
