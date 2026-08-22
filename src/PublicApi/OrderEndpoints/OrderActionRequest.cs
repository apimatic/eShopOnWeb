namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderActionRequest : BaseRequest
{
    public OrderActionRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; set; }
}
