namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; init; }

    public DispatchOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}
