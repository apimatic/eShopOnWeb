using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record NumbersV1PortInRequestList
{
    /// <summary>
    /// The SID of the Port-in request
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_in_request_sid")]
    public string? PortInRequestSid { get; init; }

    /// <summary>
    /// Status of the Port In Request
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("port_in_request_status")]
    public string? PortInRequestStatus { get; init; }

    /// <summary>
    /// The last updated timestamp of the request
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("status_last_updated_timestamp")]
    public string? StatusLastUpdatedTimestamp { get; init; }

    /// <summary>
    /// Amount of phone numbers requested
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_numbers_requested")]
    public int? PhoneNumbersRequested { get; init; }

    /// <summary>
    /// Amount of phone numbers ported
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone_numbers_ported")]
    public int? PhoneNumbersPorted { get; init; }

    /// <summary>
    /// Suggested action on this ticket
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("suggested_action")]
    public string? SuggestedAction { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
