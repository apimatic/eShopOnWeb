using System.Text.Json.Serialization;
using PayPal.Core.Models;
using PayPal.Models.Enums;

namespace PayPal.Models;

/// <summary>
/// The details of the refund status.
/// </summary>
public record RefundStatusDetails
{
    /// <summary>
    /// The reason why the refund has the <c>PENDING</c> or <c>FAILED</c> status.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reason")]
    public RefundIncompleteReason? Reason { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
