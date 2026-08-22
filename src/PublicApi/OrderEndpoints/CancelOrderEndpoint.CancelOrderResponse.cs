namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
