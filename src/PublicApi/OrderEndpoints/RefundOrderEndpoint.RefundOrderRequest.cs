using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to give back, in the currency the order was paid in. Omit to refund everything still refundable.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Why the money is given back (kept with the refund).</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Optional. Re-sending a request with the same key on the same order returns the first refund instead of
    /// making a second one. Also accepted as the <c>Idempotency-Key</c> header.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>The operator, from the bearer token.</summary>
    [JsonIgnore]
    public string RequestedBy { get; set; } = string.Empty;
}
