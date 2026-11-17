namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CancelOrderRequest
{
    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}
