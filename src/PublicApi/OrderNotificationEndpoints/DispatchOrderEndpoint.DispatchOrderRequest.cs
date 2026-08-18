namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class DispatchOrderRequest : BaseRequest
{
    public DispatchOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; set; }
}
