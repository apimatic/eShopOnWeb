namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
