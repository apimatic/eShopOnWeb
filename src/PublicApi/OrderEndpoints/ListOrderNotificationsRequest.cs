namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }

    public ListOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }
}
