namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class FulfilOrderRequest
{
    public FulfilOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}
