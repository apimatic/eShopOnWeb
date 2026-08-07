namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public RefundOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; init; }
}
