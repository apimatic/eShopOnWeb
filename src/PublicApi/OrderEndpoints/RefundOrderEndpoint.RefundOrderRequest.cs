namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Partial amount; omit to refund the remaining refundable balance in full.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key; a repeat under the same key returns the original refund instead of refunding twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? NoteToPayer { get; set; }
}
