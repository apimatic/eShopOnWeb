namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key: repeating a request under the same key does not refund twice, while
    /// two distinct partial refunds use two distinct keys.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    // Set from the route / token; never bound from the request body.
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
}
