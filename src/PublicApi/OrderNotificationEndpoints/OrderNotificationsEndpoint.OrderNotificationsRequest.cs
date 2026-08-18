namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class OrderNotificationsRequest : BaseRequest
{
    public OrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; set; }
}
