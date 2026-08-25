namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>JSON body for POST api/orders/{orderId}/pay. Supply exactly one of Card / PaymentMethodId.</summary>
public class PayOrderRequestBody
{
    public CardDetailsDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    public PayOrderRequest(string buyerId, int orderId, CardDetailsDto? card, int? paymentMethodId)
    {
        BuyerId = buyerId;
        OrderId = orderId;
        Card = card;
        PaymentMethodId = paymentMethodId;
    }

    public string BuyerId { get; }
    public int OrderId { get; }
    public CardDetailsDto? Card { get; }
    public int? PaymentMethodId { get; }
}
