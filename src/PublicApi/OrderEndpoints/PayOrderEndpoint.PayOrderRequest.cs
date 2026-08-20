namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardDetailsDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}
