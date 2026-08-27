using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds the captured payment of a fulfilled order, in full or in part.
/// <see cref="IdempotencyKey"/> is caller-supplied: repeating the request under the same
/// key returns the original refund instead of refunding twice; distinct keys remain
/// legitimate separate partial refunds.
/// </summary>
public class RefundOrderRequest : BaseRequest
{
    /// <summary>Partial amount; omit to refund the remaining refundable balance.</summary>
    [Range(0.01, 1000000)]
    public decimal? Amount { get; set; }

    [Required]
    [MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;
}
