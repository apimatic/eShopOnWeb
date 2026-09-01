namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>Payment-related settings surfaced to the domain (currency comes from configuration).</summary>
public class PaymentOptions
{
    public string Currency { get; set; } = "USD";
}
