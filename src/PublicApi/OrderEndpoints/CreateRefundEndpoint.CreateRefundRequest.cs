using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateRefundRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>Amount to give back, in the payment's currency. Omit to refund everything still refundable.</summary>
    public decimal? Amount { get; set; }
}
