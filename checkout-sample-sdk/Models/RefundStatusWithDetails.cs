using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The refund status with details.
/// </summary>
public record RefundStatusWithDetails
{
    /// <summary>
    /// The status of the refund.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public RefundStatus? Status { get; init; }

    /// <summary>
    /// The details of the refund status.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_details")]
    public RefundStatusDetails? StatusDetails { get; init; }
}
