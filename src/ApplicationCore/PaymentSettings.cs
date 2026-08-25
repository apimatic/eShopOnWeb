namespace Microsoft.eShopWeb;

/// <summary>The subset of the "PayPal" configuration section that ApplicationCore needs directly.</summary>
public class PaymentSettings
{
    public string Currency { get; set; } = "USD";
}
