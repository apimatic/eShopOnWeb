namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a fulfilled order in full (omit amount) or in part. The idempotency key is
/// mandatory: replaying the same key never refunds twice; a new key allows a distinct
/// partial refund of the same capture.
/// </summary>
public class RefundOrderRequest : BaseRequest
{
    /// <summary>Omit for a refund of everything still refundable on the order.</summary>
    public decimal? Amount { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}
