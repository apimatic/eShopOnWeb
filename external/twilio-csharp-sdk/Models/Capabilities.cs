using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

/// <summary>
/// The set of Boolean properties that indicate whether a phone number can receive calls or messages.  Capabilities are: <c>Voice</c>, <c>SMS</c>, and <c>MMS</c> and each capability can be: <c>true</c> or <c>false</c>., A mapping of capabilities this hosted phone number will have enabled on Twilio's platform., Set of booleans describing the capabilities hosted on Twilio's platform. SMS is currently only supported., The capabilities of the phone number.
/// </summary>
public record Capabilities
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("mms")]
    public bool? Mms { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sms")]
    public bool? Sms { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("voice")]
    public bool? Voice { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fax")]
    public bool? Fax { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
