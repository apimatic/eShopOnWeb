namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Refunds an order's payment in full. Order id comes from the route.</summary>
public class RefundOrderRequest : BaseRequest
{
    public RefundOrderRequest()
    {
    }

    public RefundOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; set; }
}
