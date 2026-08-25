namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    // Card payment fields (optional if using saved card)
    public string? CardNumber { get; set; }
    public int? CardExpiryMonth { get; set; }
    public int? CardExpiryYear { get; set; }
    public string? CardCvv { get; set; }
    public string? CardholderName { get; set; }
    public string? BillingCountryCode { get; set; }
    public string? BillingPostalCode { get; set; }

    // Saved card payment (optional if using card details)
    public int? PaymentMethodId { get; set; }
}
