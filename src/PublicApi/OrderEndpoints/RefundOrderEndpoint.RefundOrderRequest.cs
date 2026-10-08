using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>The amount to give back; omit it to refund everything that can still be refunded.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Optional: FRAUD, CUSTOMER REQUEST, RETURN, DUPLICATE or OTHER.</summary>
    public string? Reason { get; set; }

    /// <summary>Set from the route, never from the body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Set from the access token, never from the body.</summary>
    [JsonIgnore]
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>Set from the optional Idempotency-Key header.</summary>
    [JsonIgnore]
    public string? IdempotencyKey { get; set; }
}
