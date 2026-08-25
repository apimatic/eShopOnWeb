namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Body of POST api/orders/{orderId}/refunds. Amount null means refund whatever remains captured.</summary>
public class RefundOrderRequestBody
{
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderRequest : BaseRequest
{
    public RefundOrderRequest(int orderId, RefundOrderRequestBody body)
    {
        OrderId = orderId;
        Amount = body.Amount;
        IdempotencyKey = body.IdempotencyKey;
    }

    public int OrderId { get; }
    public decimal? Amount { get; }
    public string IdempotencyKey { get; }
}
