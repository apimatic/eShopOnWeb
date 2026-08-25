using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    [FromRoute(Name = "orderId")]
    public int OrderId { get; set; }

    [FromBody]
    public RefundOrderBody Body { get; set; } = new();
}

public class RefundOrderBody
{
    /// <summary>Omit for a full refund of whatever remains captured.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied key: repeating a request under the same key returns the original refund instead of refunding twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
