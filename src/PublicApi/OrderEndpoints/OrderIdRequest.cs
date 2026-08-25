namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Shared request shape for operator actions that need only the order id from the route.</summary>
public class OrderIdRequest : BaseRequest
{
    public int OrderId { get; }

    public OrderIdRequest(int orderId)
    {
        OrderId = orderId;
    }
}
