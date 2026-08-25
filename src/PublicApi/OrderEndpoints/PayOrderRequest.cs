namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseMessage
{
    public string Type { get; set; } = "card";  // "card" or "savedCard"

    // Raw card payment
    public string? CardNumber { get; set; }
    public string? CardExpiry { get; set; }     // YYYY-MM
    public string? CardCvv { get; set; }
    public string? CardholderName { get; set; }

    // Saved card payment
    public int? PaymentMethodId { get; set; }
}
