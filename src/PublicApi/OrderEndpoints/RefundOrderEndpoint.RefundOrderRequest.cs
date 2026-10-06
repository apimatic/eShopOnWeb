using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund in the payment currency (e.g. 12.50). Omit to refund everything still refundable.</summary>
    public decimal? Amount { get; set; }

    public string? Reason { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>The operator, taken from the caller's token.</summary>
    [JsonIgnore]
    public string RequestedBy { get; set; } = string.Empty;
}
