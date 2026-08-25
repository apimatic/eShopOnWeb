using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    [JsonIgnore] public int OrderId { get; set; }

    /// <summary>Omit for a full refund of whatever remains captured and unrefunded.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Required. Repeating a request with the same key returns the original refund instead of refunding again.</summary>
    public string IdempotencyKey { get; set; } = "";

    public string? Note { get; set; }
}
