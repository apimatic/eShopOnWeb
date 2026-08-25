namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>JSON body for POST api/orders/{orderId}/refunds. Omit Amount for a full refund of
/// whatever remains captured.</summary>
public class RefundOrderRequestBody
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = default!;
}

public class RefundOrderRequest : BaseRequest
{
    public RefundOrderRequest(string buyerId, int orderId, decimal? amount, string idempotencyKey)
    {
        BuyerId = buyerId;
        OrderId = orderId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
    }

    public string BuyerId { get; }
    public int OrderId { get; }
    public decimal? Amount { get; }
    public string IdempotencyKey { get; }
}
