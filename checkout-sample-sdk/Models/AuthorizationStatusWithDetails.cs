using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The status fields and status details for an authorized payment.
/// </summary>
public record AuthorizationStatusWithDetails
{
    /// <summary>
    /// The status for the authorized payment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status")]
    public AuthorizationStatus? Status { get; init; }

    /// <summary>
    /// The details of the authorized payment status.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_details")]
    public AuthorizationStatusDetails? StatusDetails { get; init; }
}
