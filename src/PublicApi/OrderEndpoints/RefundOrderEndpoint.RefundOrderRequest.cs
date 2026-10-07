using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to give back, in the order's currency (for example 12.50). At most what is still refundable.</summary>
    public decimal Amount { get; set; }

    /// <summary>Optional: Fraud, CustomerRequest, Return, Duplicate or Other.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Optional key (up to 64 characters) that makes a retried request safe: a second request with the same key
    /// returns the first refund instead of creating another one.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>From the route.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }
}
