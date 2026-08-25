using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The details of the authorized payment status.
/// </summary>
public record AuthorizationStatusDetails
{
    /// <summary>
    /// The reason why the authorized status is <c>PENDING</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reason")]
    public AuthorizationIncompleteReason? Reason { get; init; }
}
