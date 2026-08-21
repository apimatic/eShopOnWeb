namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardDetailsRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public OrderDto Order { get; set; } = new();
}
