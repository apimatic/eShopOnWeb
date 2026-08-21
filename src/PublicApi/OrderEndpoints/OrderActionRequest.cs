namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderActionRequest : BaseRequest
{
    public int OrderId { get; init; }

    public OrderActionRequest(int orderId)
    {
        OrderId = orderId;
    }
}
