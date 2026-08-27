namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>
    /// Partial amount to refund; omit for the full remaining refundable amount.
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key
    /// returns the original refund instead of refunding twice.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
