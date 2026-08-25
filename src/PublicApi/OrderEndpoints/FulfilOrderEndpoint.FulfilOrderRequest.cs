namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; init; }

    public FulfilOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}
