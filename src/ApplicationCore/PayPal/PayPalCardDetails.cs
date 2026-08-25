namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public class PayPalCardDetails
{
    public string Number { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public PayPalBillingAddress? BillingAddress { get; set; }
}
