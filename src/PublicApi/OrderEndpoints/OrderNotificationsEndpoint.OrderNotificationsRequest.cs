namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }

    public OrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }
}
