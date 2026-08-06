namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Request to fully refund an order's payment. Order and buyer come from the route/token.</summary>
public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; private set; }
    public string? BuyerId { get; private set; }

    public void SetRouteAndBuyer(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }
}
