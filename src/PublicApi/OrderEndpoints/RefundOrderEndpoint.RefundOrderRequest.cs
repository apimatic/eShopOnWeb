namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Set by the endpoint from the route - ignore any value supplied by the client.</summary>
    public int OrderId { get; set; }

    /// <summary>Set by the endpoint from the caller's JWT - ignore any value supplied by the client.</summary>
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>Omit for a full refund of whatever remains captured; set for a partial refund.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Repeating a request with the same key never refunds twice; a different key is a distinct refund.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
