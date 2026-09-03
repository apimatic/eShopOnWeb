using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record NumbersV1PortingBulkPhoneNumberUpdateDetail
{
    [JsonPropertyName("port_in_phone_number_sid")]
    public required string PortInPhoneNumberSid { get; init; }

    [JsonPropertyName("current_status")]
    public required string CurrentStatus { get; init; }

    [JsonPropertyName("requested_status")]
    public required string RequestedStatus { get; init; }

    /// <summary>
    /// Error message explaining why the update failed
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
