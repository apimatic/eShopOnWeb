namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }

    public GetOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }
}
