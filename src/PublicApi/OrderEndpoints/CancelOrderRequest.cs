namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest
{
    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
