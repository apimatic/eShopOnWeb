namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int? PaymentMethodId { get; set; }
    public CardPaymentDetails? Card { get; set; }
}

public class CardPaymentDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
}
