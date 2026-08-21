namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>An operator action targeting a single order by id.</summary>
public class OrderActionRequest : BaseRequest
{
    public OrderActionRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}
