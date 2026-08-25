namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Omit for a full refund of whatever remains captured; set for a partial refund.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key: repeating a request with the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = default!;
}
