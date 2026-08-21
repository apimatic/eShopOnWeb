namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderRequest
{
    public DispatchOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class DispatchOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
