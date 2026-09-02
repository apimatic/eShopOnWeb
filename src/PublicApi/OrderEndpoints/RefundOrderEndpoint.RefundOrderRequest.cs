namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Partial amount; omit for a full refund of the remaining captured balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key returns the
    /// original refund instead of refunding twice; a distinct key is a distinct refund.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
