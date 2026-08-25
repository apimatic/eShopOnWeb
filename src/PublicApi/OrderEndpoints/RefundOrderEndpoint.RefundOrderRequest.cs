namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Set by the endpoint from the route - any client-supplied value is ignored.</summary>
    public int OrderId { get; set; }

    /// <summary>Set by the endpoint from the caller's JWT identity - any client-supplied value is ignored.</summary>
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request with the same key returns the
    /// original refund rather than refunding twice; two distinct keys against the same capture
    /// are two legitimate partial refunds.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
